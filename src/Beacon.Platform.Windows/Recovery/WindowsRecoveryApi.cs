using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Beacon.Platform.Windows.Sessions;

namespace Beacon.Platform.Windows.Recovery;

public sealed class WindowsRecoveryApi : IWindowsRecoveryApi
{
    private const int SwMinimize = 6;
    private const uint WmClose = 0x0010;

    public int CurrentProcessId => Environment.ProcessId;

    public IReadOnlyList<WindowsRecoveryWindow> EnumerateTopLevelWindows()
    {
        var windows = new List<WindowsRecoveryWindow>();
        NativeMethods.EnumWindows((handle, parameter) =>
        {
            bool visible = NativeMethods.IsWindowVisible(handle);
            _ = NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
            if (processId == 0 || !NativeMethods.GetWindowRect(handle, out NativeRect rect))
            {
                return true;
            }

            windows.Add(new WindowsRecoveryWindow(
                handle,
                checked((int)processId),
                GetWindowTitle(handle),
                new WindowsRectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
                visible));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public void MoveWindow(IntPtr handle, int x, int y, int width, int height)
    {
        if (!NativeMethods.MoveWindow(handle, x, y, width, height, repaint: true))
        {
            throw new InvalidOperationException($"MoveWindow failed for handle 0x{handle.ToInt64():X}.");
        }
    }

    public void MinimizeWindow(IntPtr handle) =>
        _ = NativeMethods.ShowWindow(handle, SwMinimize);

    public void CloseWindow(IntPtr handle) =>
        _ = NativeMethods.PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);

    public void TerminateProcess(int processId)
    {
        using Process process = Process.GetProcessById(processId);
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
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
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
        public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveWindow(IntPtr handle, int x, int y, int width, int height, bool repaint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);
    }
}
