using System.Collections.Concurrent;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Platform.Windows.Streaming;

public sealed record StreamWorkerCommandResponse(
    WorkerIpcEnvelope Completion,
    IReadOnlyList<WorkerIpcEnvelope> Events);

public sealed class StreamWorkerNamedPipeClient : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly Task<int> processExit;
    private readonly uint expectedProcessId;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly ConcurrentDictionary<ulong, PendingRequest> pending = new();
    private readonly CancellationTokenSource disposal = new();
    private readonly TaskCompletionSource lifecycleCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? receiveLoop;
    private Exception? terminalError;
    private long nextRequestId;
    private int initialized;
    private int disposed;

    public StreamWorkerNamedPipeClient(Stream stream, Task<int> processExit, uint expectedProcessId)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        this.processExit = processExit ?? throw new ArgumentNullException(nameof(processExit));
        this.expectedProcessId = expectedProcessId;
    }

    public bool IsReady => Volatile.Read(ref initialized) == 1 && Volatile.Read(ref disposed) == 0;

    public Task Completion => lifecycleCompletion.Task;

    public Exception? TerminalError => Volatile.Read(ref terminalError);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref initialized, -1, 0) != 0)
        {
            throw new InvalidOperationException("StreamWorker pipe client was already initialized.");
        }

        try
        {
            WorkerIpcEnvelope hello = await ReadEnvelopeOrProcessExitAsync(cancellationToken).ConfigureAwait(false);
            ProtocolVersion.EnsureSupported(hello.ProtocolVersion);
            if (hello.BodyCase != WorkerIpcEnvelope.BodyOneofCase.WorkerHello
                || hello.WorkerHello.ProcessId != expectedProcessId
                || hello.WorkerHello.WorkerInstanceId.IsEmpty)
            {
                throw new StreamWorkerProtocolException("StreamWorker hello identity is invalid.");
            }

            WorkerIpcEnvelope ready = await ReadEnvelopeOrProcessExitAsync(cancellationToken).ConfigureAwait(false);
            ProtocolVersion.EnsureSupported(ready.ProtocolVersion);
            if (ready.BodyCase != WorkerIpcEnvelope.BodyOneofCase.WorkerReady
                || !ready.WorkerReady.WorkerInstanceId.Equals(hello.WorkerHello.WorkerInstanceId))
            {
                throw new StreamWorkerProtocolException("StreamWorker readiness identity is invalid.");
            }

            Volatile.Write(ref initialized, 1);
            receiveLoop = ReceiveLoopAsync();
        }
        catch
        {
            Volatile.Write(ref initialized, 0);
            throw;
        }
    }

    public async Task<StreamWorkerCommandResponse> SendAsync(
        WorkerIpcEnvelope request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!IsReady)
        {
            throw new InvalidOperationException("StreamWorker is not ready.");
        }

        WorkerIpcEnvelope command = request.Clone();
        command.ProtocolVersion = ProtocolVersion.Current;
        command.RequestId = checked((ulong)Interlocked.Increment(ref nextRequestId));
        var operation = new PendingRequest();
        if (!pending.TryAdd(command.RequestId, operation))
        {
            throw new InvalidOperationException("StreamWorker request id collision.");
        }

        try
        {
            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[] frame = ProtobufLengthFrameCodec.Encode(command);
                await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeGate.Release();
            }

            Task winner = await Task.WhenAny(operation.Task, processExit)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (winner == processExit)
            {
                int exitCode = await processExit.ConfigureAwait(false);
                throw new StreamWorkerProcessExitedException(exitCode);
            }

            return await operation.Task.ConfigureAwait(false);
        }
        finally
        {
            pending.TryRemove(command.RequestId, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        disposal.Cancel();
        Volatile.Write(ref initialized, 0);
        FailPending(new ObjectDisposedException(nameof(StreamWorkerNamedPipeClient)));
        if (receiveLoop is not null)
        {
            try
            {
                await receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (disposal.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }
        }
        lifecycleCompletion.TrySetResult();
        await stream.DisposeAsync().ConfigureAwait(false);
        disposal.Dispose();
        writeGate.Dispose();
    }

    private async Task<WorkerIpcEnvelope> ReadEnvelopeOrProcessExitAsync(CancellationToken cancellationToken)
    {
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            disposal.Token);
        Task<WorkerIpcEnvelope> read = ReadEnvelopeAsync(readCancellation.Token);
        try
        {
            Task winner = await Task.WhenAny(read, processExit)
                .WaitAsync(readCancellation.Token)
                .ConfigureAwait(false);
            if (winner == processExit)
            {
                readCancellation.Cancel();
                await ObserveCompletionAsync(read).ConfigureAwait(false);
                throw new StreamWorkerProcessExitedException(await processExit.ConfigureAwait(false));
            }

            return await read.ConfigureAwait(false);
        }
        catch
        {
            readCancellation.Cancel();
            await ObserveCompletionAsync(read).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!disposal.IsCancellationRequested)
            {
                WorkerIpcEnvelope envelope = await ReadEnvelopeAsync(disposal.Token).ConfigureAwait(false);
                ProtocolVersion.EnsureSupported(envelope.ProtocolVersion);
                if (envelope.RequestId != 0 && pending.TryGetValue(envelope.RequestId, out PendingRequest? request))
                {
                    if (envelope.BodyCase == WorkerIpcEnvelope.BodyOneofCase.WorkerCompletion)
                    {
                        pending.TryRemove(envelope.RequestId, out _);
                        request.Complete(envelope);
                    }
                    else
                    {
                        request.AddEvent(envelope);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (disposal.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Volatile.Write(ref terminalError, error);
            FailPending(error);
        }
        finally
        {
            Volatile.Write(ref initialized, 0);
            lifecycleCompletion.TrySetResult();
        }
    }

    private async Task<WorkerIpcEnvelope> ReadEnvelopeAsync(CancellationToken cancellationToken)
    {
        byte[] prefix = new byte[sizeof(uint)];
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        int messageLength = ProtobufLengthFrameCodec.ReadMessageLength(prefix);
        byte[] frame = new byte[sizeof(uint) + messageLength];
        prefix.CopyTo(frame, 0);
        if (messageLength > 0)
        {
            await stream.ReadExactlyAsync(
                frame.AsMemory(sizeof(uint), messageLength),
                cancellationToken).ConfigureAwait(false);
        }

        return ProtobufLengthFrameCodec.Decode(frame, WorkerIpcEnvelope.Parser);
    }

    private void FailPending(Exception error)
    {
        foreach ((ulong requestId, PendingRequest request) in pending)
        {
            if (pending.TryRemove(requestId, out _))
            {
                request.Fail(error);
            }
        }
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private sealed class PendingRequest
    {
        private readonly object sync = new();
        private readonly List<WorkerIpcEnvelope> events = [];
        private readonly TaskCompletionSource<StreamWorkerCommandResponse> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StreamWorkerCommandResponse> Task => completion.Task;

        public void AddEvent(WorkerIpcEnvelope value)
        {
            lock (sync)
            {
                events.Add(value);
            }
        }

        public void Complete(WorkerIpcEnvelope value)
        {
            WorkerIpcEnvelope[] snapshot;
            lock (sync)
            {
                snapshot = events.ToArray();
            }

            completion.TrySetResult(new StreamWorkerCommandResponse(value, snapshot));
        }

        public void Fail(Exception error) => completion.TrySetException(error);
    }
}

public sealed class StreamWorkerProtocolException : Exception
{
    public StreamWorkerProtocolException(string message)
        : base(message)
    {
    }
}

public sealed class StreamWorkerProcessExitedException : Exception
{
    public StreamWorkerProcessExitedException(int exitCode)
        : base($"Beacon StreamWorker exited with code {exitCode}.")
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
