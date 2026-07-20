namespace Beacon.Platform.Windows.Displays;

internal sealed class WindowsDisplayLeaseTopologyReconciler(
    Func<DisplayTopologySnapshot> queryTopology,
    Func<DisplayApiResult> applyExtendedTopology)
{
    private string diagnostic = "No leased virtual display topology reconciliation has been required.";

    public string Diagnostic => Volatile.Read(ref diagnostic);

    public ValueTask ReconcileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DisplayTopologySnapshot topology = queryTopology();
        if (IsValidExtendedTopology(topology))
        {
            Volatile.Write(ref diagnostic, "Leased physical and virtual display topology is active.");
            return ValueTask.CompletedTask;
        }

        DisplayApiResult result = applyExtendedTopology();
        if (!result.Success)
        {
            string error = result.Error ?? "Unable to reactivate leased virtual display topology.";
            Volatile.Write(ref diagnostic, error);
            throw new InvalidOperationException(error);
        }

        DisplayTopologySnapshot repaired = queryTopology();
        if (!IsValidExtendedTopology(repaired))
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

    private static bool IsValidExtendedTopology(DisplayTopologySnapshot topology) =>
        !topology.IsMirrorMode
        && topology.Paths.Any(path => path.Kind == DisplayPathKind.Physical)
        && topology.Paths.Any(path => path.Kind == DisplayPathKind.Virtual);
}
