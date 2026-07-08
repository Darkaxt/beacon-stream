namespace Beacon.Core.Displays;

public interface IDisplayBackend
{
    Task<DisplayHealth> GetHealthAsync(CancellationToken cancellationToken);

    Task<DisplayEnsureResult> PrepareVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        HdrPreference hdrPreference,
        CancellationToken cancellationToken);

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

public sealed record DisplayHealth(
    bool DriverReady,
    string Diagnostic,
    bool TopologyAvailable,
    bool MirrorMode,
    bool PhysicalPrimaryVerified,
    IReadOnlyList<DisplayPathHealth> Paths)
{
    public static DisplayHealth Unknown(string diagnostic) =>
        new(
            DriverReady: false,
            Diagnostic: diagnostic,
            TopologyAvailable: false,
            MirrorMode: false,
            PhysicalPrimaryVerified: false,
            Paths: []);
}

public sealed record DisplayPathHealth(
    string DisplayId,
    string Kind,
    int Width,
    int Height,
    int RefreshHz,
    bool IsPrimary,
    int X,
    int Y);

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
