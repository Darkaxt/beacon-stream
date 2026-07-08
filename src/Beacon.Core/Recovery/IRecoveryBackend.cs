namespace Beacon.Core.Recovery;

public interface IRecoveryBackend
{
    Task<RecoveryActionResult> MoveWindowsBackAsync(bool minimize, CancellationToken cancellationToken);

    Task<RecoveryActionResult> CloseVirtualWindowsAsync(CancellationToken cancellationToken);

    Task<RecoveryActionResult> TerminateVirtualProcessesAsync(CancellationToken cancellationToken);
}

public sealed record RecoveryActionResult(
    bool Success,
    string Action,
    int AffectedCount,
    IReadOnlyList<string> Diagnostics,
    string? Error)
{
    public static RecoveryActionResult Ok(
        string action,
        int affectedCount,
        IReadOnlyList<string>? diagnostics = null) =>
        new(true, action, affectedCount, diagnostics ?? [], null);

    public static RecoveryActionResult Fail(
        string action,
        string error,
        IReadOnlyList<string>? diagnostics = null) =>
        new(false, action, 0, diagnostics ?? [], error);
}
