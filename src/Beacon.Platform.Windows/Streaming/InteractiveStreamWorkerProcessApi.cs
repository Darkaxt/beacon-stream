using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Beacon.Platform.Windows.Streaming;

internal interface IInteractiveStreamWorkerProcessApi
{
    uint ResumeThread(SafeFileHandle threadHandle);

    void TerminateProcess(SafeFileHandle processHandle);

    void WaitForExit(SafeFileHandle processHandle);
}

internal sealed class WindowsInteractiveStreamWorkerProcessApi : IInteractiveStreamWorkerProcessApi
{
    private const uint Infinite = uint.MaxValue;
    private const uint WaitObject0 = 0;
    private const uint TerminatedExitCode = 1;

    public static WindowsInteractiveStreamWorkerProcessApi Instance { get; } = new();

    private WindowsInteractiveStreamWorkerProcessApi()
    {
    }

    public uint ResumeThread(SafeFileHandle threadHandle) =>
        StreamWorkerNativeMethods.ResumeThread(threadHandle);

    public void TerminateProcess(SafeFileHandle processHandle)
    {
        if (!TerminateProcessNative(processHandle, TerminatedExitCode))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not terminate unassigned suspended StreamWorker.");
        }
    }

    public void WaitForExit(SafeFileHandle processHandle)
    {
        if (WaitForSingleObject(processHandle, Infinite) != WaitObject0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not wait for unassigned suspended StreamWorker termination.");
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "TerminateProcess", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcessNative(SafeFileHandle processHandle, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);
}
