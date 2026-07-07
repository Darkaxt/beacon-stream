namespace Beacon.Core.Displays;

public interface IDisplayBackend
{
    Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        HdrPreference hdrPreference,
        CancellationToken cancellationToken);

    Task RestorePhysicalPrimaryAsync(CancellationToken cancellationToken);

    Task RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken);
}

public sealed record DisplayEnsureResult(bool Success, string? Error, bool HdrEnabled = false, string? HdrReason = null)
{
    public static DisplayEnsureResult Ok(bool hdrEnabled = false, string? hdrReason = null) =>
        new(true, null, hdrEnabled, hdrReason);

    public static DisplayEnsureResult Fail(string error) => new(false, error);
}
