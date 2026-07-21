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
    private string diagnostic = "No leased virtual display topology reconciliation has been required.";

    public string Diagnostic => Volatile.Read(ref diagnostic);

    public ValueTask ReconcileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<LeasedDisplayTopologyRequirement> requirements = queryRequirements();
        if (requirements.Count == 0)
        {
            WriteDiagnostic("requirements=0 phase=skipped");
            Volatile.Write(ref diagnostic, "No activated leased display topology requires reconciliation.");
            return ValueTask.CompletedTask;
        }

        DisplayTopologySnapshot topology = queryTopology();
        WriteDiagnostic(
            $"requirements={requirements.Count} phase=observed physical={topology.Paths.Any(path => path.Kind == DisplayPathKind.Physical)} topology={topology.Fingerprint}");
        if (IsValidExtendedTopology(topology, requirements))
        {
            WriteDiagnostic($"requirements={requirements.Count} phase=valid");
            Volatile.Write(ref diagnostic, "Leased physical and virtual display topology is active.");
            return ValueTask.CompletedTask;
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

        Volatile.Write(
            ref diagnostic,
            "Leased display topology transition requested; awaiting verification on the next heartbeat.");
        return ValueTask.CompletedTask;
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
}
