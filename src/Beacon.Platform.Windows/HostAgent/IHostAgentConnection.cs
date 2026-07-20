using Beacon.HostAgent.Contracts;

namespace Beacon.Platform.Windows.HostAgent;

public interface IHostAgentConnection : IAsyncDisposable
{
    HostAgentConnectionState State { get; }

    Task RunAsync(CancellationToken cancellationToken);

    Task<HostAgentResponse> SendAsync<TPayload>(
        HostAgentOperation operation,
        TPayload payload,
        CancellationToken cancellationToken);

    Task<HostAgentConnectionState> WaitForStateChangeAsync(
        long afterRevision,
        CancellationToken cancellationToken);
}

public sealed record HostAgentConnectionState(
    long Revision,
    bool Connected,
    string Diagnostic,
    HostAgentStatusPayload? Status);

public sealed class HostAgentUnavailableException : InvalidOperationException
{
    public HostAgentUnavailableException(string message)
        : base(message)
    {
    }
}
