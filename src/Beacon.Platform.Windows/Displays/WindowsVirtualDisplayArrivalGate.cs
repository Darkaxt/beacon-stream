namespace Beacon.Platform.Windows.Displays;

internal sealed record VirtualDisplayTargetArrivalSnapshot(
    bool Available,
    string? DisplayName,
    string TopologyFingerprint);

internal sealed class WindowsVirtualDisplayArrivalGate(
    Func<long> heartbeatRevision,
    Func<long, CancellationToken, Task<long>> waitForHeartbeat)
{
    public async Task<VirtualDisplayTargetArrivalSnapshot> WaitForStableTargetAsync(
        Func<VirtualDisplayTargetArrivalSnapshot> queryTarget,
        CancellationToken cancellationToken)
    {
        VirtualDisplayTargetArrivalSnapshot? previous = null;
        long observedRevision = heartbeatRevision();

        while (true)
        {
            observedRevision = await waitForHeartbeat(observedRevision, cancellationToken)
                .ConfigureAwait(false);
            VirtualDisplayTargetArrivalSnapshot current = queryTarget();
            if (current.Available && current == previous)
            {
                return current;
            }

            previous = current.Available ? current : null;
        }
    }
}
