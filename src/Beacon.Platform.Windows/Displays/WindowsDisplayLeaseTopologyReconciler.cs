namespace Beacon.Platform.Windows.Displays;

internal sealed record LeasedDisplayTopologyRequirement(
    string DisplayId,
    int Width,
    int Height,
    int RefreshHz);

internal enum LeasedDisplayRecoveryAction
{
    None,
    ReactivateLeasedDisplays
}

internal static class WindowsDisplayLeaseRecoveryPlanner
{
    public static LeasedDisplayRecoveryAction Plan(
        DisplayTopologySnapshot topology,
        IReadOnlyList<LeasedDisplayTopologyRequirement> requirements)
    {
        bool exactLeasedTopology = !topology.IsMirrorMode
            && topology.Paths.Any(path => path.Kind == DisplayPathKind.Physical)
            && requirements.All(requirement => topology.Paths.Any(path =>
                path.Kind == DisplayPathKind.Virtual
                && string.Equals(path.DisplayId, requirement.DisplayId, StringComparison.Ordinal)
                && path.Width == requirement.Width
                && path.Height == requirement.Height
                && path.RefreshHz == requirement.RefreshHz));
        return exactLeasedTopology
            ? LeasedDisplayRecoveryAction.None
            : LeasedDisplayRecoveryAction.ReactivateLeasedDisplays;
    }
}

internal sealed class WindowsDisplayLeaseTopologyReconciler(
    Func<DisplayTopologySnapshot> queryTopology,
    Func<IReadOnlyList<LeasedDisplayTopologyRequirement>> queryRequirements,
    Func<IReadOnlyList<LeasedDisplayTopologyRequirement>, DisplayApiResult> applyLeasedTopology,
    Action<string>? diagnosticSink = null)
{
    private readonly SemaphoreSlim transitionGate = new(1, 1);
    private string diagnostic = "No leased virtual display topology reconciliation has been required.";
    private PendingTopologyTransition? pendingTransition;

    public string Diagnostic => Volatile.Read(ref diagnostic);

    public async ValueTask ReconcileAsync(CancellationToken cancellationToken)
    {
        await transitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<LeasedDisplayTopologyRequirement> requirements = queryRequirements();
            if (requirements.Count == 0)
            {
                pendingTransition = null;
                WriteDiagnostic("requirements=0 phase=skipped");
                Volatile.Write(ref diagnostic, "No activated leased display topology requires reconciliation.");
                return;
            }

            DisplayTopologySnapshot topology = queryTopology();
            WriteDiagnostic(
                $"requirements={requirements.Count} phase=observed physical={topology.Paths.Any(path => path.Kind == DisplayPathKind.Physical)} topology={topology.Fingerprint}");
            if (IsValidExtendedTopology(topology, requirements))
            {
                pendingTransition = null;
                WriteDiagnostic($"requirements={requirements.Count} phase=valid");
                Volatile.Write(ref diagnostic, "Leased physical and virtual display topology is active.");
                return;
            }

            if (pendingTransition is not null)
            {
                if (!string.Equals(
                    pendingTransition.ObservedTopologyFingerprint,
                    topology.Fingerprint,
                    StringComparison.Ordinal))
                {
                    pendingTransition = new PendingTopologyTransition(
                        topology.Fingerprint,
                        TopologyChangeObserved: true);
                    WriteDiagnostic(
                        $"requirements={requirements.Count} phase=topology-change-observed topology={topology.Fingerprint}");
                    Volatile.Write(
                        ref diagnostic,
                        "Windows topology changed after the transition; awaiting another heartbeat to verify that it has stabilized.");
                    return;
                }

                if (!pendingTransition.TopologyChangeObserved)
                {
                    WriteDiagnostic(
                        $"requirements={requirements.Count} phase=transition-pending topology={topology.Fingerprint}");
                    Volatile.Write(
                        ref diagnostic,
                        "Leased display topology transition is awaiting Windows topology change.");
                    return;
                }

                WriteDiagnostic(
                    $"requirements={requirements.Count} phase=topology-stable topology={topology.Fingerprint}");
                pendingTransition = null;
            }

            DisplayApiResult result = applyLeasedTopology(requirements);
            WriteDiagnostic(
                $"requirements={requirements.Count} phase=transition-completed success={result.Success} error={result.Error ?? "none"}");
            if (!result.Success)
            {
                string error = result.Error ?? "Unable to reactivate leased virtual display topology.";
                Volatile.Write(ref diagnostic, error);
                throw new InvalidOperationException(error);
            }

            pendingTransition = new PendingTopologyTransition(
                topology.Fingerprint,
                TopologyChangeObserved: false);
            Volatile.Write(
                ref diagnostic,
                "Leased display topology transition requested; awaiting verification on the next heartbeat.");
        }
        finally
        {
            transitionGate.Release();
        }
    }

    private void WriteDiagnostic(string message)
    {
        try
        {
            diagnosticSink?.Invoke($"reconciler {message}");
        }
        catch
        {
        }
    }

    private static bool IsValidExtendedTopology(
        DisplayTopologySnapshot topology,
        IReadOnlyList<LeasedDisplayTopologyRequirement> requirements) =>
        WindowsDisplayLeaseRecoveryPlanner.Plan(topology, requirements) ==
        LeasedDisplayRecoveryAction.None;

    private sealed record PendingTopologyTransition(
        string ObservedTopologyFingerprint,
        bool TopologyChangeObserved);
}
