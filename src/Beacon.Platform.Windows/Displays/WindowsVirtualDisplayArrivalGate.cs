namespace Beacon.Platform.Windows.Displays;

internal sealed record VirtualDisplayTargetArrivalSnapshot(
    bool Available,
    string? DisplayName,
    string TopologyFingerprint,
    bool ExtendedTopology = false,
    bool DesiredTopology = false);

internal sealed class WindowsVirtualDisplayArrivalGate(
    Func<long> heartbeatRevision,
    Func<long, CancellationToken, Task<long>> waitForHeartbeat,
    Action<string>? diagnostic = null)
{
    public async Task<DisplayApiResult> ApplyAfterNextHeartbeatAsync(
        Func<DisplayApiResult> applyStateTransition,
        CancellationToken cancellationToken)
    {
        long observedRevision = heartbeatRevision();
        WriteDiagnostic($"gate=post-driver-add phase=waiting afterRevision={observedRevision}");
        observedRevision = await waitForHeartbeat(observedRevision, cancellationToken)
            .ConfigureAwait(false);
        WriteDiagnostic($"gate=post-driver-add phase=heartbeat-observed revision={observedRevision}");
        DisplayApiResult result = applyStateTransition();
        WriteDiagnostic(
            $"gate=post-driver-add phase=transition-completed success={result.Success} error={result.Error ?? "none"}");
        return result;
    }

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
            WriteDiagnostic(
                $"gate=target-arrival phase=observed revision={observedRevision} available={current.Available} display={current.DisplayName ?? "none"} topology={current.TopologyFingerprint}");
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

    public async Task<DisplayApiResult> WaitForStableDesiredTopologyAsync(
        Func<VirtualDisplayTargetArrivalSnapshot> queryTarget,
        CancellationToken cancellationToken) =>
        await WaitForStableStateAsync(
            queryTarget,
            snapshot => snapshot.DesiredTopology,
            applyStateTransition: null,
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

    public async Task<DisplayApiResult> WaitForStableExtendedTopologyAsync(
        Func<VirtualDisplayTargetArrivalSnapshot> queryTarget,
        CancellationToken cancellationToken) =>
        await WaitForStableStateAsync(
            queryTarget,
            snapshot => snapshot.ExtendedTopology,
            applyStateTransition: null,
            cancellationToken).ConfigureAwait(false);

    private async Task<DisplayApiResult> WaitForStableStateAsync(
        Func<VirtualDisplayTargetArrivalSnapshot> queryTarget,
        Func<VirtualDisplayTargetArrivalSnapshot, bool> isDesiredState,
        Func<VirtualDisplayTargetArrivalSnapshot, DisplayApiResult>? applyStateTransition,
        CancellationToken cancellationToken)
    {
        VirtualDisplayTargetArrivalSnapshot? previousDesired = null;
        long observedRevision = heartbeatRevision();

        while (true)
        {
            observedRevision = await waitForHeartbeat(observedRevision, cancellationToken)
                .ConfigureAwait(false);
            VirtualDisplayTargetArrivalSnapshot current = queryTarget();
            WriteDiagnostic(
                $"gate=desired-topology phase=observed revision={observedRevision} available={current.Available} extended={current.ExtendedTopology} desired={current.DesiredTopology} topology={current.TopologyFingerprint}");
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
            if (applyStateTransition is null)
            {
                WriteDiagnostic(
                    $"gate=desired-topology phase=awaiting-heartbeat-owner topology={current.TopologyFingerprint}");
                continue;
            }

            DisplayApiResult applyResult = applyStateTransition(current);
            if (!applyResult.Success)
            {
                return applyResult;
            }

            WriteDiagnostic(
                $"gate=desired-topology phase=transition-completed success=True topology={current.TopologyFingerprint}");
        }
    }

    private void WriteDiagnostic(string message)
    {
        try
        {
            diagnostic?.Invoke(message);
        }
        catch
        {
        }
    }
}
