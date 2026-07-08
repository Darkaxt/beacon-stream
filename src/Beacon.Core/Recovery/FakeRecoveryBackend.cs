namespace Beacon.Core.Recovery;

public sealed class FakeRecoveryBackend : IRecoveryBackend
{
    public List<bool> MoveWindowsBackCalls { get; } = [];

    public int CloseVirtualWindowsCalls { get; private set; }

    public int TerminateVirtualProcessesCalls { get; private set; }

    public RecoveryActionResult MoveWindowsBackResult { get; set; } =
        RecoveryActionResult.Ok("move-windows-back", 0, ["fake recovery backend"]);

    public RecoveryActionResult CloseVirtualWindowsResult { get; set; } =
        RecoveryActionResult.Ok("close-virtual-windows", 0, ["fake recovery backend"]);

    public RecoveryActionResult TerminateVirtualProcessesResult { get; set; } =
        RecoveryActionResult.Ok("terminate-virtual-processes", 0, ["fake recovery backend"]);

    public Task<RecoveryActionResult> MoveWindowsBackAsync(bool minimize, CancellationToken cancellationToken)
    {
        MoveWindowsBackCalls.Add(minimize);
        return Task.FromResult(MoveWindowsBackResult);
    }

    public Task<RecoveryActionResult> CloseVirtualWindowsAsync(CancellationToken cancellationToken)
    {
        CloseVirtualWindowsCalls++;
        return Task.FromResult(CloseVirtualWindowsResult);
    }

    public Task<RecoveryActionResult> TerminateVirtualProcessesAsync(CancellationToken cancellationToken)
    {
        TerminateVirtualProcessesCalls++;
        return Task.FromResult(TerminateVirtualProcessesResult);
    }
}
