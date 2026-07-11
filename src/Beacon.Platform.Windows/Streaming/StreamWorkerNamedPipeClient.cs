using System.Collections.Concurrent;
using System.Threading.Channels;
using Beacon.Core.Input;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Stream.V1;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Google.Protobuf;

namespace Beacon.Platform.Windows.Streaming;

public sealed record StreamWorkerCommandResponse(
    WorkerIpcEnvelope Completion,
    IReadOnlyList<WorkerIpcEnvelope> Events);

public sealed class StreamWorkerNamedPipeClient : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly Task<int> processExit;
    private readonly uint expectedProcessId;
    private readonly long processGeneration;
    private readonly ChannelWriter<StreamWorkerEvent> eventWriter;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly ConcurrentDictionary<ulong, PendingRequest> pending = new();
    private readonly CancellationTokenSource disposal = new();
    private readonly TaskCompletionSource lifecycleCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? receiveLoop;
    private Exception? terminalError;
    private byte[] workerInstanceId = [];
    private long nextRequestId;
    private int initialized;
    private int disposed;

    public StreamWorkerNamedPipeClient(Stream stream, Task<int> processExit, uint expectedProcessId)
        : this(
            stream,
            processExit,
            expectedProcessId,
            processGeneration: 1,
            Channel.CreateBounded<StreamWorkerEvent>(1).Writer)
    {
    }

    public StreamWorkerNamedPipeClient(
        Stream stream,
        Task<int> processExit,
        uint expectedProcessId,
        long processGeneration,
        ChannelWriter<StreamWorkerEvent> eventWriter)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        this.processExit = processExit ?? throw new ArgumentNullException(nameof(processExit));
        this.expectedProcessId = expectedProcessId;
        this.processGeneration = processGeneration > 0
            ? processGeneration
            : throw new ArgumentOutOfRangeException(nameof(processGeneration));
        this.eventWriter = eventWriter ?? throw new ArgumentNullException(nameof(eventWriter));
    }

    public bool IsReady => Volatile.Read(ref initialized) == 1 && Volatile.Read(ref disposed) == 0;

    public Task Completion => lifecycleCompletion.Task;

    public Exception? TerminalError => Volatile.Read(ref terminalError);

    public ReadOnlyMemory<byte> WorkerInstanceId => workerInstanceId;

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
            workerInstanceId = hello.WorkerHello.WorkerInstanceId.ToByteArray();

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
                if (envelope.RequestId == 0)
                {
                    StreamWorkerEvent workerEvent = TranslateEvent(envelope);
                    await eventWriter.WriteAsync(workerEvent, disposal.Token).ConfigureAwait(false);
                }
                else if (pending.TryGetValue(envelope.RequestId, out PendingRequest? request))
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

    private StreamWorkerEvent TranslateEvent(WorkerIpcEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.SessionId))
        {
            throw new StreamWorkerProtocolException("StreamWorker event session identity is invalid.");
        }

        return envelope.BodyCase switch
        {
            WorkerIpcEnvelope.BodyOneofCase.TransportAuthenticated =>
                TranslateTransportAuthenticated(envelope),
            WorkerIpcEnvelope.BodyOneofCase.TransportDisconnected =>
                TranslateTransportDisconnected(envelope),
            WorkerIpcEnvelope.BodyOneofCase.InputReceived => TranslateInput(envelope),
            WorkerIpcEnvelope.BodyOneofCase.FeedbackReceived => TranslateFeedback(envelope),
            WorkerIpcEnvelope.BodyOneofCase.MediaEvidence => TranslateMediaEvidence(envelope),
            _ => throw new StreamWorkerProtocolException("StreamWorker emitted an unknown unsolicited event."),
        };
    }

    private StreamWorkerTransportAuthenticated TranslateTransportAuthenticated(WorkerIpcEnvelope envelope)
    {
        if (envelope.TransportAuthenticated.SessionGeneration == 0)
        {
            throw new StreamWorkerProtocolException("StreamWorker transport event generation is invalid.");
        }
        return new StreamWorkerTransportAuthenticated(
            processGeneration,
            envelope.SessionId,
            envelope.TransportAuthenticated.SessionGeneration,
            envelope.TransportAuthenticated.MaximumDatagramBytes);
    }

    private StreamWorkerTransportDisconnected TranslateTransportDisconnected(WorkerIpcEnvelope envelope)
    {
        if (envelope.TransportDisconnected.SessionGeneration == 0)
        {
            throw new StreamWorkerProtocolException("StreamWorker disconnect event generation is invalid.");
        }
        return new StreamWorkerTransportDisconnected(
            processGeneration,
            envelope.SessionId,
            envelope.TransportDisconnected.SessionGeneration);
    }

    private StreamWorkerInputReceived TranslateInput(WorkerIpcEnvelope envelope)
    {
        InputReceived received = envelope.InputReceived;
        InputStreamEnvelope input = received.Input;
        if (received.SessionGeneration == 0
            || input is null
            || input.ProtocolVersion != ProtocolVersion.Current
            || !string.Equals(input.SessionId, envelope.SessionId, StringComparison.Ordinal)
            || input.InputBatch is null
            || input.InputBatch.Events.Count == 0)
        {
            throw new StreamWorkerProtocolException("StreamWorker input event is invalid.");
        }

        ClientInputEvent[] translated = input.InputBatch.Events.Select(TranslateInputEvent).ToArray();
        return new StreamWorkerInputReceived(
            processGeneration,
            envelope.SessionId,
            received.SessionGeneration,
            input.Sequence,
            translated);
    }

    private static ClientInputEvent TranslateInputEvent(InputEvent input) => input.BodyCase switch
    {
        InputEvent.BodyOneofCase.Pointer when input.Pointer.Action != PointerAction.Unspecified =>
            ClientInputEvent.StreamPointer(
                input.Pointer.Action switch
                {
                    PointerAction.Move => ClientPointerAction.Move,
                    PointerAction.ButtonDown => ClientPointerAction.ButtonDown,
                    PointerAction.ButtonUp => ClientPointerAction.ButtonUp,
                    PointerAction.Scroll => ClientPointerAction.Scroll,
                    _ => throw new StreamWorkerProtocolException("StreamWorker pointer action is invalid."),
                },
                input.Pointer.X,
                input.Pointer.Y,
                input.Pointer.WheelDelta,
                input.Pointer.Button),
        InputEvent.BodyOneofCase.Keyboard =>
            ClientInputEvent.StreamKeyboard(input.Keyboard.ScanCode, input.Keyboard.Pressed),
        InputEvent.BodyOneofCase.Controller =>
            ClientInputEvent.StreamController(
                input.Controller.ControllerIndex,
                input.Controller.ControlId,
                input.Controller.Value),
        InputEvent.BodyOneofCase.Touch when input.Touch.Action != TouchAction.Unspecified
            && input.Touch.CoordinateDenominator != 0
            && input.Touch.PressureDenominator != 0 =>
            ClientInputEvent.StreamTouch(
                input.Touch.ContactId,
                input.Touch.Action switch
                {
                    TouchAction.Down => ClientTouchAction.Down,
                    TouchAction.Move => ClientTouchAction.Move,
                    TouchAction.Up => ClientTouchAction.Up,
                    TouchAction.Cancel => ClientTouchAction.Cancel,
                    _ => throw new StreamWorkerProtocolException("StreamWorker touch action is invalid."),
                },
                input.Touch.XNumerator,
                input.Touch.YNumerator,
                input.Touch.CoordinateDenominator,
                input.Touch.PressureNumerator,
                input.Touch.PressureDenominator),
        _ => throw new StreamWorkerProtocolException("StreamWorker input variant is invalid."),
    };

    private StreamWorkerFeedbackReceived TranslateFeedback(WorkerIpcEnvelope envelope)
    {
        FeedbackReceived received = envelope.FeedbackReceived;
        FeedbackStreamEnvelope feedback = received.Feedback;
        if (received.SessionGeneration == 0
            || feedback is null
            || feedback.ProtocolVersion != ProtocolVersion.Current
            || !string.Equals(feedback.SessionId, envelope.SessionId, StringComparison.Ordinal))
        {
            throw new StreamWorkerProtocolException("StreamWorker feedback event is invalid.");
        }

        (StreamWorkerFeedbackKind kind, ulong primary, ulong secondary, uint count) = feedback.BodyCase switch
        {
            FeedbackStreamEnvelope.BodyOneofCase.RenderedFrame =>
                (StreamWorkerFeedbackKind.RenderedFrame,
                    feedback.RenderedFrame.FrameSequence,
                    feedback.RenderedFrame.PresentationTimeUs,
                    0u),
            FeedbackStreamEnvelope.BodyOneofCase.DatagramLoss =>
                (StreamWorkerFeedbackKind.DatagramLoss,
                    feedback.DatagramLoss.FrameSequence,
                    0UL,
                    checked((uint)feedback.DatagramLoss.MissingChunkIndexes.Count)),
            FeedbackStreamEnvelope.BodyOneofCase.Decoder =>
                (StreamWorkerFeedbackKind.Decoder,
                    checked((ulong)feedback.Decoder.State),
                    feedback.Decoder.PlatformErrorCode,
                    0u),
            FeedbackStreamEnvelope.BodyOneofCase.QueueDepth =>
                (StreamWorkerFeedbackKind.QueueDepth,
                    feedback.QueueDepth.QueuedAccessUnits,
                    feedback.QueueDepth.DroppedAccessUnits,
                    0u),
            FeedbackStreamEnvelope.BodyOneofCase.BenchmarkEvidence =>
                (StreamWorkerFeedbackKind.Benchmark,
                    feedback.BenchmarkEvidence.SchemaVersion,
                    0UL,
                    checked((uint)feedback.BenchmarkEvidence.Facts.Count)),
            _ => throw new StreamWorkerProtocolException("StreamWorker feedback variant is invalid."),
        };
        return new StreamWorkerFeedbackReceived(
            processGeneration,
            envelope.SessionId,
            received.SessionGeneration,
            feedback.Sequence,
            kind,
            primary,
            secondary,
            count);
    }

    private StreamWorkerMediaEvidence TranslateMediaEvidence(WorkerIpcEnvelope envelope)
    {
        MediaEvidence media = envelope.MediaEvidence;
        if (media.SessionGeneration == 0)
        {
            throw new StreamWorkerProtocolException("StreamWorker media event generation is invalid.");
        }
        return new StreamWorkerMediaEvidence(
            processGeneration,
            envelope.SessionId,
            media.SessionGeneration,
            media.Sequence,
            media.PresentationTimeUs,
            media.DatagramBytes);
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
