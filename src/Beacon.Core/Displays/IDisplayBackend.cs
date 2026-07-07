namespace Beacon.Core.Displays;

public interface IDisplayBackend
{
    Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        CancellationToken cancellationToken);

    Task RestorePhysicalPrimaryAsync(CancellationToken cancellationToken);

    Task RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken);
}

public sealed record DisplayEnsureResult(bool Success, string? Error)
{
    public static DisplayEnsureResult Ok() => new(true, null);

    public static DisplayEnsureResult Fail(string error) => new(false, error);
}
