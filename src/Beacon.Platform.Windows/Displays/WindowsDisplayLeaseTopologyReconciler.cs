namespace Beacon.Platform.Windows.Displays;

internal sealed record LeasedDisplayTopologyRequirement(
    string DisplayId,
    int Width,
    int Height,
    int RefreshHz);

internal enum LeasedDisplayRecoveryAction
{
    None,
    ReattachPhysical,
    ReactivateLeasedDisplays
}

internal static class WindowsDisplayLeaseRecoveryPlanner
{
    public static LeasedDisplayRecoveryAction Plan(
        DisplayTopologySnapshot topology,
        IReadOnlyList<LeasedDisplayTopologyRequirement> requirements)
    {
        if (!topology.Paths.Any(path => path.Kind == DisplayPathKind.Physical))
        {
            return LeasedDisplayRecoveryAction.ReattachPhysical;
        }

        bool exactLeasedTopology = !topology.IsMirrorMode
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
    Func<IReadOnlyList<LeasedDisplayTopologyRequirement>, DisplayApiResult> applyLeasedTopology)
{
    private string diagnostic = "No leased virtual display topology reconciliation has been required.";

    public string Diagnostic => Volatile.Read(ref diagnostic);

    public ValueTask ReconcileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<LeasedDisplayTopologyRequirement> requirements = queryRequirements();
        if (requirements.Count == 0)
        {
            Volatile.Write(ref diagnostic, "No activated leased display topology requires reconciliation.");
            return ValueTask.CompletedTask;
        }

        DisplayTopologySnapshot topology = queryTopology();
        if (IsValidExtendedTopology(topology, requirements))
        {
            Volatile.Write(ref diagnostic, "Leased physical and virtual display topology is active.");
            return ValueTask.CompletedTask;
        }

        DisplayApiResult result = applyLeasedTopology(requirements);
        if (!result.Success)
        {
            string error = result.Error ?? "Unable to reactivate leased virtual display topology.";
            Volatile.Write(ref diagnostic, error);
            throw new InvalidOperationException(error);
        }

        DisplayTopologySnapshot repaired = queryTopology();
        if (!IsValidExtendedTopology(repaired, requirements))
        {
            string error =
                "Leased physical and virtual display topology repair was not verified. " +
                $"LastTopology={repaired.Fingerprint}.";
            Volatile.Write(ref diagnostic, error);
            throw new InvalidOperationException(error);
        }

        Volatile.Write(
            ref diagnostic,
            "Leased physical and virtual display topology reactivated after Windows removed an active path.");
        return ValueTask.CompletedTask;
    }

    private static bool IsValidExtendedTopology(
        DisplayTopologySnapshot topology,
        IReadOnlyList<LeasedDisplayTopologyRequirement> requirements) =>
        WindowsDisplayLeaseRecoveryPlanner.Plan(topology, requirements) ==
        LeasedDisplayRecoveryAction.None;
}
