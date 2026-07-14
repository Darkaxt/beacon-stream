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

internal sealed class NoOpWindowsDisplayLeaseSession : IWindowsDisplayLeaseSession
{
    public static NoOpWindowsDisplayLeaseSession Instance { get; } = new();

    public SudoVdaDriverLeaseSessionSnapshot Snapshot { get; } = new(
        LeaseCount: 0,
        WatchdogTimeoutSeconds: null,
        HeartbeatActive: false,
        Healthy: true,
        Diagnostic: "No Windows driver lease session is attached.");

    public Task<SudoVdaDriverLeaseHoldResult> HoldAsync(
        string displayId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SudoVdaDriverLeaseHoldResult.Held());
    }

    public Task ReleaseAsync(string displayId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
