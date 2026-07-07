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

    Task<DisplayRestoreResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken);

    Task<DisplayRemoveResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken);
}

public sealed record DisplayEnsureResult(bool Success, string? Error, bool HdrEnabled = false, string? HdrReason = null)
{
    public static DisplayEnsureResult Ok(bool hdrEnabled = false, string? hdrReason = null) =>
        new(true, null, hdrEnabled, hdrReason);

    public static DisplayEnsureResult Fail(string error) => new(false, error);
}

public sealed record DisplayRestoreResult(bool Success, string? Error)
{
    public static DisplayRestoreResult Ok() => new(true, null);

    public static DisplayRestoreResult Fail(string error) => new(false, error);
}

public sealed record DisplayRemoveResult(bool Success, string? Error)
{
    public static DisplayRemoveResult Ok() => new(true, null);

    public static DisplayRemoveResult Fail(string error) => new(false, error);
}

public sealed record DisplayRecoveryResult(bool Success, string? Error)
{
    public static DisplayRecoveryResult Ok() => new(true, null);

    public static DisplayRecoveryResult Fail(string error) => new(false, error);
}
