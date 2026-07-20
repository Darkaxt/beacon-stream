namespace Beacon.Platform.Windows.Displays;

public interface IWindowsDisplayApi
{
    DisplayDriverStatus GetDriverStatus();

    Task<DisplayApiResult> CreateVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        CancellationToken cancellationToken);

    Task<DisplayTopologySnapshot> QueryTopologyAsync(CancellationToken cancellationToken);

    Task<DisplayApiResult> SetVirtualPrimaryAsync(string displayId, CancellationToken cancellationToken);

    Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken);

    Task<DisplayApiResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken);

    Task<DisplayHdrCapability> QueryHdrCapabilityAsync(string displayId, CancellationToken cancellationToken);
}

public sealed record DisplayDriverStatus(
    bool Ready,
    string Diagnostic,
    byte? ProtocolMajor = null,
    byte? ProtocolMinor = null,
    byte? ProtocolIncremental = null);

public sealed record DisplayApiResult(bool Success, string? Error)
{
    public static DisplayApiResult Ok() => new(true, null);

    public static DisplayApiResult Fail(string error) => new(false, error);
}

public sealed record DisplayHdrCapability(bool Supported, bool Enabled, string Reason);
