namespace Beacon.Platform.Windows.Displays;

internal sealed record VirtualDisplayTargetArrivalSnapshot(
    bool Available,
    string? DisplayName,
    string TopologyFingerprint,
    bool DesiredTopology = false);

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

    public async Task<DisplayApiResult> WaitForStableDesiredTopologyAsync(
        Func<VirtualDisplayTargetArrivalSnapshot> queryTarget,
        Func<DisplayApiResult> applyDesiredTopology,
        CancellationToken cancellationToken)
    {
        VirtualDisplayTargetArrivalSnapshot? previous = null;
        long observedRevision = heartbeatRevision();

        while (true)
        {
            observedRevision = await waitForHeartbeat(observedRevision, cancellationToken)
                .ConfigureAwait(false);
            VirtualDisplayTargetArrivalSnapshot current = queryTarget();
            if (!current.Available)
            {
                previous = null;
                continue;
            }

            if (current != previous)
            {
                previous = current;
                continue;
            }

            if (current.DesiredTopology)
            {
                return DisplayApiResult.Ok();
            }

            DisplayApiResult applyResult = applyDesiredTopology();
            if (!applyResult.Success)
            {
                return applyResult;
            }

            previous = null;
        }
    }
}
