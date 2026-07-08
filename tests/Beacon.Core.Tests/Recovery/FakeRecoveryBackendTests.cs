using Beacon.Core.Recovery;

namespace Beacon.Core.Tests.Recovery;

public sealed class FakeRecoveryBackendTests
{
    [Fact]
    public async Task MoveWindowsBackRecordsMinimizeChoice()
    {
        var backend = new FakeRecoveryBackend
        {
            MoveWindowsBackResult = RecoveryActionResult.Ok("move-windows-back", 2, ["moved 2 windows"])
        };

        RecoveryActionResult result = await backend.MoveWindowsBackAsync(minimize: true, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("move-windows-back", result.Action);
        Assert.Equal(2, result.AffectedCount);
        Assert.Equal([true], backend.MoveWindowsBackCalls);
    }

    [Fact]
    public async Task CloseAndTerminateRecordCalls()
    {
        var backend = new FakeRecoveryBackend
        {
            CloseVirtualWindowsResult = RecoveryActionResult.Ok("close-virtual-windows", 3),
            TerminateVirtualProcessesResult = RecoveryActionResult.Ok("terminate-virtual-processes", 1)
        };

        RecoveryActionResult close = await backend.CloseVirtualWindowsAsync(CancellationToken.None);
        RecoveryActionResult terminate = await backend.TerminateVirtualProcessesAsync(CancellationToken.None);

        Assert.True(close.Success);
        Assert.True(terminate.Success);
        Assert.Equal(3, close.AffectedCount);
        Assert.Equal(1, terminate.AffectedCount);
        Assert.Equal(1, backend.CloseVirtualWindowsCalls);
        Assert.Equal(1, backend.TerminateVirtualProcessesCalls);
    }
}
