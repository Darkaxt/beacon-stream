using System.Security.Principal;
using Beacon.HostAgent.Contracts;

namespace Beacon.Platform.Windows.HostAgent;

public sealed class HostAgentConnection : IHostAgentConnection
{
    private readonly Lock gate = new();
    private readonly IHostAgentConnector connector;
    private readonly CancellationTokenSource disposal = new();
    private TaskCompletionSource<HostAgentConnectionState> stateChanged = NewStateChange();
    private HostAgentConnectionState state = new(
        Revision: 0,
        Connected: false,
        Diagnostic: "Beacon Host Agent connection has not started.",
        Status: null);
    private HostAgentNamedPipeClient? activeClient;
    private int started;
    private int disposed;

    public HostAgentConnection(SecurityIdentifier owner)
        : this(new WindowsHostAgentConnector(
            owner ?? throw new ArgumentNullException(nameof(owner))))
    {
    }

    internal HostAgentConnection(IHostAgentConnector connector)
    {
        this.connector = connector ?? throw new ArgumentNullException(nameof(connector));
    }

    public HostAgentConnectionState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
        {
            throw new InvalidOperationException("Beacon Host Agent connection is already running.");
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            disposal.Token);
        CancellationToken stopping = linkedCancellation.Token;
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                HostAgentNamedPipeClient? client = null;
                try
                {
                    Stream stream = await connector.ConnectAsync(stopping).ConfigureAwait(false);
                    client = new HostAgentNamedPipeClient(stream);
                    lock (gate)
                    {
                        activeClient = client;
                    }

                    HostAgentResponse handshake = await client.SendAsync(
                        HostAgentOperation.GetStatus,
                        new EmptyHostAgentPayload(),
                        stopping).ConfigureAwait(false);
                    if (!handshake.Success)
                    {
                        throw new HostAgentProtocolException(
                            $"Host Agent handshake failed: {handshake.ResultCode}.");
                    }

                    HostAgentStatusPayload status =
                        HostAgentProtocol.ReadPayload<HostAgentStatusPayload>(handshake.Payload);
                    PublishState(
                        connected: true,
                        diagnostic: "Beacon Host Agent connected.",
                        status);
                    await client.Completion.WaitAsync(stopping).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (HostAgentProtocolException error)
                {
                    PublishState(connected: false, error.Message, status: null);
                    throw;
                }
                catch (Exception error) when (
                    error is HostAgentConnectionLostException or IOException or EndOfStreamException)
                {
                    PublishState(
                        connected: false,
                        "Beacon Host Agent disconnected; awaiting the next Agent pipe.",
                        status: null);
                }
                finally
                {
                    lock (gate)
                    {
                        if (ReferenceEquals(activeClient, client))
                        {
                            activeClient = null;
                        }
                    }

                    if (client is not null)
                    {
                        await client.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            PublishState(
                connected: false,
                "Beacon Host Agent connection stopped.",
                status: null);
        }
    }

    public Task<HostAgentResponse> SendAsync<TPayload>(
        HostAgentOperation operation,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        HostAgentNamedPipeClient client;
        lock (gate)
        {
            client = activeClient is not null && state.Connected
                ? activeClient
                : throw new HostAgentUnavailableException(state.Diagnostic);
        }

        return client.SendAsync(operation, payload, cancellationToken);
    }

    public Task<HostAgentConnectionState> WaitForStateChangeAsync(
        long afterRevision,
        CancellationToken cancellationToken)
    {
        Task<HostAgentConnectionState> wait;
        lock (gate)
        {
            if (state.Revision > afterRevision)
            {
                return Task.FromResult(state);
            }
            wait = stateChanged.Task;
        }

        return wait.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        disposal.Cancel();
        HostAgentNamedPipeClient? client;
        lock (gate)
        {
            client = activeClient;
            activeClient = null;
        }
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        disposal.Dispose();
    }

    private void PublishState(
        bool connected,
        string diagnostic,
        HostAgentStatusPayload? status)
    {
        TaskCompletionSource<HostAgentConnectionState> previous;
        HostAgentConnectionState next;
        lock (gate)
        {
            if (state.Connected == connected
                && string.Equals(state.Diagnostic, diagnostic, StringComparison.Ordinal)
                && Equals(state.Status, status))
            {
                return;
            }

            next = new HostAgentConnectionState(
                checked(state.Revision + 1),
                connected,
                diagnostic,
                status);
            state = next;
            previous = stateChanged;
            stateChanged = NewStateChange();
        }
        previous.TrySetResult(next);
    }

    private static TaskCompletionSource<HostAgentConnectionState> NewStateChange() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
