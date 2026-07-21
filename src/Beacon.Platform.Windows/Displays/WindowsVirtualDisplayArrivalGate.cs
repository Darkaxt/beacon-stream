namespace Beacon.Platform.Windows.Displays;

internal sealed record VirtualDisplayTargetArrivalSnapshot(
    bool Available,
    string? DisplayName,
    string TopologyFingerprint,
    bool ExtendedTopology = false,
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
        Func<VirtualDisplayTargetArrivalSnapshot, DisplayApiResult> applyDesiredTopology,
        CancellationToken cancellationToken) =>
        await WaitForStableStateAsync(
            queryTarget,
            snapshot => snapshot.DesiredTopology,
            applyDesiredTopology,
            cancellationToken).ConfigureAwait(false);

    public async Task<DisplayApiResult> WaitForStableExtendedTopologyAsync(
        Func<VirtualDisplayTargetArrivalSnapshot> queryTarget,
        Func<VirtualDisplayTargetArrivalSnapshot, DisplayApiResult> applyExtendedTopology,
        CancellationToken cancellationToken) =>
        await WaitForStableStateAsync(
            queryTarget,
            snapshot => snapshot.ExtendedTopology,
            applyExtendedTopology,
            cancellationToken).ConfigureAwait(false);

    private async Task<DisplayApiResult> WaitForStableStateAsync(
        Func<VirtualDisplayTargetArrivalSnapshot> queryTarget,
        Func<VirtualDisplayTargetArrivalSnapshot, bool> isDesiredState,
        Func<VirtualDisplayTargetArrivalSnapshot, DisplayApiResult> applyStateTransition,
        CancellationToken cancellationToken)
    {
        VirtualDisplayTargetArrivalSnapshot? previousDesired = null;
        long observedRevision = heartbeatRevision();

        while (true)
        {
            observedRevision = await waitForHeartbeat(observedRevision, cancellationToken)
                .ConfigureAwait(false);
            VirtualDisplayTargetArrivalSnapshot current = queryTarget();
            if (!current.Available)
            {
                previousDesired = null;
                continue;
            }

            if (isDesiredState(current))
            {
                if (current == previousDesired)
                {
                    return DisplayApiResult.Ok();
                }

                previousDesired = current;
                continue;
            }

            previousDesired = null;
            DisplayApiResult applyResult = applyStateTransition(current);
            if (!applyResult.Success)
            {
                return applyResult;
            }
        }
    }
}
