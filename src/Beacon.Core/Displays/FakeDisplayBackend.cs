namespace Beacon.Core.Displays;

public sealed class FakeDisplayBackend : IDisplayBackend
{
    public bool AllowEnsure { get; set; } = true;

    public List<string> EnsureCalls { get; } = [];

    public List<string> RestoreCalls { get; } = [];

    public List<string> RemoveCalls { get; } = [];

    public Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        CancellationToken cancellationToken)
    {
        EnsureCalls.Add($"{displayId}:{width}x{height}@{refreshHz}");
        return Task.FromResult(AllowEnsure
            ? DisplayEnsureResult.Ok()
            : DisplayEnsureResult.Fail("virtual display is unavailable"));
    }

    public Task RestorePhysicalPrimaryAsync(CancellationToken cancellationToken)
    {
        RestoreCalls.Add("physical-primary");
        return Task.CompletedTask;
    }

    public Task RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken)
    {
        RemoveCalls.Add(displayId);
        return Task.CompletedTask;
    }
}
