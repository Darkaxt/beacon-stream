using System.Runtime.InteropServices;

namespace Beacon.Platform.Windows.Displays;

internal sealed record WindowsChildProcessExitCodeResult(
    bool Success,
    uint ExitCode,
    int Win32Error);

internal sealed class WindowsChildProcessCompletionGate
{
    private const uint Infinite = uint.MaxValue;
    private const uint WaitObject0 = 0;
    private readonly Func<IntPtr, uint, uint> waitForSingleObject;
    private readonly Func<IntPtr, WindowsChildProcessExitCodeResult> getExitCode;
    private readonly Func<int> getLastWin32Error;

    public WindowsChildProcessCompletionGate()
        : this(
            NativeMethods.WaitForSingleObject,
            process => NativeMethods.GetExitCodeProcess(process, out uint exitCode)
                ? new WindowsChildProcessExitCodeResult(true, exitCode, 0)
                : new WindowsChildProcessExitCodeResult(
                    false,
                    0,
                    Marshal.GetLastWin32Error()),
            Marshal.GetLastWin32Error)
    {
    }

    internal WindowsChildProcessCompletionGate(
        Func<IntPtr, uint, uint> waitForSingleObject,
        Func<IntPtr, WindowsChildProcessExitCodeResult> getExitCode,
        Func<int>? getLastWin32Error = null)
    {
        this.waitForSingleObject = waitForSingleObject;
        this.getExitCode = getExitCode;
        this.getLastWin32Error = getLastWin32Error ?? Marshal.GetLastWin32Error;
    }

    public DisplayApiResult Wait(IntPtr process)
    {
        uint waitStatus = waitForSingleObject(process, Infinite);
        if (waitStatus != WaitObject0)
        {
            return DisplayApiResult.Fail(
                $"Waiting for the user display topology helper failed. WaitStatus={waitStatus} Win32={getLastWin32Error()}.");
        }

        WindowsChildProcessExitCodeResult exit = getExitCode(process);
        if (!exit.Success)
        {
            return DisplayApiResult.Fail(
                $"Unable to read the user display topology helper exit code. Win32={exit.Win32Error}.");
        }

        return exit.ExitCode == 0
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail(
                $"The user display topology helper failed with exit code {exit.ExitCode}.");
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    }
}
