using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent.HostUpdates;

internal interface IHostAgentUpdateGuard
{
    Task EnsureSafeAsync(CancellationToken cancellationToken);
}

internal sealed class HostAgentDisplayUpdateGuard(IHostAgentDisplayExecutor displays)
    : IHostAgentUpdateGuard
{
    public async Task EnsureSafeAsync(CancellationToken cancellationToken)
    {
        SudoVdaDriverLeaseSessionSnapshot lease = displays.GetLeaseSnapshot();
        if (lease.LeaseCount != 0)
        {
            throw Reject("Host Agent has active display leases.");
        }

        DisplayTopologySnapshot topology = await displays.QueryAsync(cancellationToken)
            .ConfigureAwait(false);
        if (topology.Paths.Any(path => path.Kind == DisplayPathKind.Virtual))
        {
            throw Reject("Host Agent has an active virtual display.");
        }
    }

    private static HostAgentUpdateStartException Reject(string message) =>
        new("host-agent-active-display", message);
}

internal sealed class AllowHostAgentUpdateGuard : IHostAgentUpdateGuard
{
    public static AllowHostAgentUpdateGuard Instance { get; } = new();

    public Task EnsureSafeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
