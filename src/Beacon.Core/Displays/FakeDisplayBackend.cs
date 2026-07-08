namespace Beacon.Core.Displays;

public sealed class FakeDisplayBackend : IDisplayBackend
{
    public bool AllowEnsure { get; set; } = true;

    public DisplayRestoreResult NextRestoreResult { get; set; } = DisplayRestoreResult.Ok();

    public DisplayRemoveResult NextRemoveResult { get; set; } = DisplayRemoveResult.Ok();

    public Queue<DisplayEnsureResult> EnsureResults { get; } = [];

    public List<string> EnsureCalls { get; } = [];

    public List<string> RestoreCalls { get; } = [];

    public List<string> RemoveCalls { get; } = [];

    public Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        HdrPreference hdrPreference,
        CancellationToken cancellationToken)
    {
        EnsureCalls.Add($"{displayId}:{width}x{height}@{refreshHz}:hdr={hdrPreference}");
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
        return Task.FromResult(NextRestoreResult);
    }

    public Task<DisplayRemoveResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken)
    {
        RemoveCalls.Add(displayId);
        return Task.FromResult(NextRemoveResult);
    }
}
