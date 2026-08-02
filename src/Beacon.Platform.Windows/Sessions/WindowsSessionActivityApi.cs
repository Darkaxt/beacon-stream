using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Beacon.Platform.Windows.Sessions;

public sealed class WindowsSessionActivityApi : IWindowsSessionActivityApi
{
    private const int ProcessBasicInformation = 0;
    private const int ShowWindowRestore = 9;

    public int CurrentProcessId => Environment.ProcessId;

    public bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public IReadOnlyList<int> GetChildProcessIds(int processId)
    {
        var children = new List<int>();
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (TryGetParentProcessId(process, out int parentProcessId) && parentProcessId == processId && !HasExited(process))
                {
                    children.Add(process.Id);
                }
            }
        }

        return children;
    }

    public DateTimeOffset? GetProcessStartTime(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return new DateTimeOffset(process.StartTime);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public IReadOnlyList<WindowsTopLevelWindow> EnumerateTopLevelWindows()
    {
        var windows = new List<WindowsTopLevelWindow>();
        NativeMethods.EnumWindows((handle, parameter) =>
        {
            bool visible = NativeMethods.IsWindowVisible(handle);
            _ = NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
            if (processId == 0 || !NativeMethods.GetWindowRect(handle, out NativeRect rect))
            {
                return true;
            }

            string title = GetWindowTitle(handle);
            windows.Add(new WindowsTopLevelWindow(
                checked((int)processId),
                title,
                new WindowsRectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
                visible)
            {
                Handle = handle,
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public WindowsTopLevelWindowActivationResult ActivateTopLevelWindow(nint windowHandle)
    {
        IntPtr handle = windowHandle;
        if (handle == IntPtr.Zero ||
            !NativeMethods.IsWindow(handle) ||
            !NativeMethods.IsWindowVisible(handle))
        {
            return WindowsTopLevelWindowActivationResult.Fail(
                "The verified session-owned window is no longer active.");
        }

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == handle)
        {
            return WindowsTopLevelWindowActivationResult.Activated();
        }

        uint currentThread = NativeMethods.GetCurrentThreadId();
        uint targetThread = NativeMethods.GetWindowThreadProcessId(handle, out _);
        uint foregroundThread = foreground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        _ = NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

        bool foregroundAttached = false;
        bool targetAttached = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                foregroundAttached = NativeMethods.AttachThreadInput(
                    currentThread,
                    foregroundThread,
                    attach: true);
            }
            if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
            {
                targetAttached = NativeMethods.AttachThreadInput(
                    currentThread,
                    targetThread,
                    attach: true);
            }

            if (NativeMethods.IsIconic(handle))
            {
                _ = NativeMethods.ShowWindow(handle, ShowWindowRestore);
            }
            _ = NativeMethods.BringWindowToTop(handle);
            _ = NativeMethods.SetForegroundWindow(handle);
            _ = NativeMethods.SetFocus(handle);
            return NativeMethods.GetForegroundWindow() == handle
                ? WindowsTopLevelWindowActivationResult.Activated()
                : WindowsTopLevelWindowActivationResult.Fail(
                    "Windows did not activate the verified session-owned window for input.");
        }
        finally
        {
            if (targetAttached)
            {
                _ = NativeMethods.AttachThreadInput(currentThread, targetThread, attach: false);
            }
            if (foregroundAttached)
            {
                _ = NativeMethods.AttachThreadInput(currentThread, foregroundThread, attach: false);
            }
        }
    }

    public async Task<bool> TerminateProcessAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        if (processId <= 0 || processId == CurrentProcessId)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return true;
            }
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
            return true;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool TryGetParentProcessId(Process process, out int parentProcessId)
    {
        parentProcessId = 0;
        try
        {
            var information = new ProcessBasicInformationBlock();
            int status = NativeMethods.NtQueryInformationProcess(
                process.Handle,
                ProcessBasicInformation,
                ref information,
                Marshal.SizeOf<ProcessBasicInformationBlock>(),
                out _);

            if (status != 0)
            {
                return false;
            }

            parentProcessId = information.InheritedFromUniqueProcessId.ToInt32();
            return parentProcessId > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException)
        {
            return false;
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        int length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = NativeMethods.GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformationBlock
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2A;
        public IntPtr Reserved2B;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    private static partial class NativeMethods
    {
        public delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr handle);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessage(
            out NativeMessage message,
            IntPtr handle,
            uint minimum,
            uint maximum,
            uint removeMessage);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref ProcessBasicInformationBlock processInformation,
            int processInformationLength,
            out int returnLength);
    }
}
