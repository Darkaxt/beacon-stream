namespace Beacon.Core.Displays;

public sealed class FakeDisplayBackend : IDisplayBackend
{
    public bool AllowEnsure { get; set; } = true;

    public DisplayHealth Health { get; set; } = new(
        DriverReady: true,
        Diagnostic: "Fake display backend ready.",
        TopologyAvailable: true,
        MirrorMode: false,
        PhysicalPrimaryVerified: true,
        Paths:
        [
            new DisplayPathHealth(
                "physical-fake",
                "Physical",
                2560,
                1600,
                120,
                IsPrimary: true,
                X: 0,
                Y: 0)
        ]);

    public DisplayRestoreResult NextRestoreResult { get; set; } = DisplayRestoreResult.Ok();

    public DisplayRemoveResult NextRemoveResult { get; set; } = DisplayRemoveResult.Ok();

    public Queue<DisplayEnsureResult> EnsureResults { get; } = [];

    public Queue<DisplayEnsureResult> PrepareResults { get; } = [];

    public Queue<DisplayRestoreResult> RestoreResults { get; } = [];

    public List<string> PrepareCalls { get; } = [];

    public List<string> EnsureCalls { get; } = [];

    public List<string> RestoreCalls { get; } = [];

    public List<string> RemoveCalls { get; } = [];

    public Task<DisplayHealth> GetHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Health);

    public Task<DisplayEnsureResult> PrepareVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        HdrPreference hdrPreference,
        CancellationToken cancellationToken)
    {
        PrepareCalls.Add(FormatCall(displayId, width, height, refreshHz, hdrPreference));
        if (PrepareResults.Count > 0)
        {
            return Task.FromResult(PrepareResults.Dequeue());
        }

        return Task.FromResult(AllowEnsure
            ? DisplayEnsureResult.Ok()
            : DisplayEnsureResult.Fail("virtual display is unavailable"));
    }

    public Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        HdrPreference hdrPreference,
        CancellationToken cancellationToken)
    {
        EnsureCalls.Add(FormatCall(displayId, width, height, refreshHz, hdrPreference));
        if (EnsureResults.Count > 0)
        {
            return Task.FromResult(EnsureResults.Dequeue());
        }

        return Task.FromResult(AllowEnsure
            ? DisplayEnsureResult.Ok()
            : DisplayEnsureResult.Fail("virtual display is unavailable"));
    }

    public Task<DisplayRestoreResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken)
    {
        RestoreCalls.Add("physical-primary");
        if (RestoreResults.Count > 0)
        {
            return Task.FromResult(RestoreResults.Dequeue());
        }

        return Task.FromResult(NextRestoreResult);
    }

    public Task<DisplayRemoveResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken)
    {
        RemoveCalls.Add(displayId);
        return Task.FromResult(NextRemoveResult);
    }

    private static string FormatCall(string displayId, int width, int height, int refreshHz, HdrPreference hdrPreference) =>
        $"{displayId}:{width}x{height}@{refreshHz}:hdr={hdrPreference}";
}
