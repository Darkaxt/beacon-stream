namespace Beacon.Platform.Windows.Displays;

public interface IWindowsDisplayLeaseSession
{
    SudoVdaDriverLeaseSessionSnapshot Snapshot { get; }

    Task<SudoVdaDriverLeaseHoldResult> HoldAsync(
        string displayId,
        CancellationToken cancellationToken);

    Task ReleaseAsync(string displayId, CancellationToken cancellationToken);
}

public sealed record SudoVdaDriverLeaseHoldResult(bool Success, bool Acquired, string? Error)
{
    public static SudoVdaDriverLeaseHoldResult Held() => new(true, true, null);

    public static SudoVdaDriverLeaseHoldResult AlreadyHeld() => new(true, false, null);

    public static SudoVdaDriverLeaseHoldResult Fail(string error) => new(false, false, error);
}

public sealed record SudoVdaDriverLeaseSessionSnapshot(
    int LeaseCount,
    uint? WatchdogTimeoutSeconds,
    bool HeartbeatActive,
    bool Healthy,
    string Diagnostic);
