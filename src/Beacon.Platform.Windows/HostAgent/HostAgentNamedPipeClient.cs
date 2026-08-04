using System.Collections.Concurrent;
using Beacon.HostAgent.Contracts;

namespace Beacon.Platform.Windows.HostAgent;

public sealed class HostAgentNamedPipeClient : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, PendingRequest> pending = new();
    private readonly ConcurrentDictionary<Guid, byte> ignoredResponses = new();
    private readonly CancellationTokenSource disposal = new();
    private readonly TaskCompletionSource completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task receiveLoop;
    private Exception? terminalError;
    private int disposed;

    public HostAgentNamedPipeClient(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        receiveLoop = ReceiveLoopAsync();
    }

    public Task Completion => completion.Task;

    public async Task<HostAgentResponse> SendAsync<TPayload>(
        HostAgentOperation operation,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        Exception? terminal = Volatile.Read(ref terminalError);
        if (terminal is not null)
        {
            throw terminal;
        }

        var request = new HostAgentRequest(
            HostAgentProtocol.CurrentVersion,
            Guid.NewGuid(),
            operation,
            HostAgentProtocol.CreatePayload(payload));
        var operationCompletion = new PendingRequest();
        if (!pending.TryAdd(request.RequestId, operationCompletion))
        {
            throw new InvalidOperationException("Host Agent request id collision.");
        }

        bool written = false;
        try
        {
            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await HostAgentFrameCodec.WriteRequestAsync(stream, request, cancellationToken)
                    .ConfigureAwait(false);
                written = true;
            }
            finally
            {
                writeGate.Release();
            }

            return await operationCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or EndOfStreamException)
        {
            var lost = new HostAgentConnectionLostException("Host Agent pipe write failed.", error);
            Terminate(lost);
            throw lost;
        }
        finally
        {
            if (pending.TryRemove(request.RequestId, out _) && written)
            {
                ignoredResponses.TryAdd(request.RequestId, 0);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        disposal.Cancel();
        await stream.DisposeAsync().ConfigureAwait(false);
        try
        {
            await receiveLoop.ConfigureAwait(false);
        }
        catch (Exception error) when (
            error is OperationCanceledException or ObjectDisposedException or IOException)
        {
        }
        finally
        {
            var disposedError = new ObjectDisposedException(nameof(HostAgentNamedPipeClient));
            FailPending(disposedError);
            completion.TrySetResult();
            disposal.Dispose();
            writeGate.Dispose();
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!disposal.IsCancellationRequested)
            {
                HostAgentResponse response = await HostAgentFrameCodec.ReadResponseAsync(
                    stream,
                    disposal.Token).ConfigureAwait(false);
                if (response.ProtocolVersion != HostAgentProtocol.CurrentVersion)
                {
                    throw new HostAgentProtocolException("Host Agent response protocol version is unsupported.");
                }

                if (pending.TryRemove(response.RequestId, out PendingRequest? request))
                {
                    request.SetResult(response);
                    continue;
                }

                if (ignoredResponses.TryRemove(response.RequestId, out _))
                {
                    continue;
                }

                throw new HostAgentProtocolException("Host Agent returned an unknown request id.");
            }
        }
        catch (OperationCanceledException) when (disposal.IsCancellationRequested)
        {
            completion.TrySetResult();
        }
        catch (Exception error) when (error is EndOfStreamException or IOException)
        {
            Terminate(new HostAgentConnectionLostException("Host Agent pipe disconnected.", error));
        }
        catch (Exception error)
        {
            Terminate(error);
        }
    }

    private void Terminate(Exception error)
    {
        if (Interlocked.CompareExchange(ref terminalError, error, null) is not null)
        {
            return;
        }

        FailPending(error);
        completion.TrySetException(error);
    }

    private void FailPending(Exception error)
    {
        foreach (KeyValuePair<Guid, PendingRequest> pair in pending)
        {
            if (pending.TryRemove(pair.Key, out PendingRequest? request))
            {
                request.SetException(error);
            }
        }
        ignoredResponses.Clear();
    }

    private sealed class PendingRequest
    {
        private readonly TaskCompletionSource<HostAgentResponse> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<HostAgentResponse> Task => completion.Task;

        public void SetResult(HostAgentResponse response) => completion.TrySetResult(response);

        public void SetException(Exception error) => completion.TrySetException(error);
    }
}

public sealed class HostAgentConnectionLostException : IOException
{
    public HostAgentConnectionLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class HostAgentProtocolException : Exception
{
    public HostAgentProtocolException(string message)
        : base(message)
    {
    }
}
