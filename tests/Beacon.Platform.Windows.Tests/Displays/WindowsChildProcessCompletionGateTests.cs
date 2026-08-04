using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsChildProcessCompletionGateTests
{
    [Fact]
    public void WaitsWithoutADeadlineAndAcceptsOnlyExitCodeZero()
    {
        uint? observedWaitMilliseconds = null;
        var gate = new WindowsChildProcessCompletionGate(
            (_, milliseconds) =>
            {
                observedWaitMilliseconds = milliseconds;
                return 0;
            },
            _ => new WindowsChildProcessExitCodeResult(true, 0, 0));

        DisplayApiResult result = gate.Wait(new IntPtr(42));

        Assert.True(result.Success);
        Assert.Equal(uint.MaxValue, observedWaitMilliseconds);
    }

    [Fact]
    public void NonzeroChildExitCodeIsReturnedToTheTopologyGate()
    {
        var gate = new WindowsChildProcessCompletionGate(
            (_, _) => 0,
            _ => new WindowsChildProcessExitCodeResult(true, 6, 0));

        DisplayApiResult result = gate.Wait(new IntPtr(42));

        Assert.False(result.Success);
        Assert.Contains("exit code 6", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedProcessWaitIsReturnedToTheTopologyGate()
    {
        var gate = new WindowsChildProcessCompletionGate(
            (_, _) => uint.MaxValue,
            _ => throw new InvalidOperationException("Exit code must not be queried."),
            () => 5);

        DisplayApiResult result = gate.Wait(new IntPtr(42));

        Assert.False(result.Success);
        Assert.Contains("Win32=5", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
