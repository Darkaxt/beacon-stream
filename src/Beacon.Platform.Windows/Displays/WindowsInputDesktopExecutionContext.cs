using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Beacon.Platform.Windows.Displays;

internal sealed class WindowsInputDesktopExecutionContext(
    IWindowsInputDesktopPlatform? platform = null)
{
    private readonly IWindowsInputDesktopPlatform platform =
        platform ?? new NativeWindowsInputDesktopPlatform();

    public T Invoke<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        T? result = default;
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            IntPtr desktop = this.platform.OpenCurrentInputDesktop();
            if (desktop == IntPtr.Zero)
            {
                failure = ExceptionDispatchInfo.Capture(new WindowsInputDesktopException(
                    $"Unable to open the current Windows input desktop. Win32={this.platform.GetLastError()}."));
                return;
            }

            try
            {
                if (!this.platform.SetCurrentThreadDesktop(desktop))
                {
                    throw new WindowsInputDesktopException(
                        $"Unable to bind a Windows display operation to the current input desktop. Win32={this.platform.GetLastError()}.");
                }

                result = operation();
            }
            catch (Exception error)
            {
                failure = ExceptionDispatchInfo.Capture(error);
            }
            finally
            {
                this.platform.CloseDesktop(desktop);
            }
        })
        {
            IsBackground = true,
            Name = "Beacon Windows input desktop operation"
        };

        thread.Start();
        thread.Join();
        failure?.Throw();
        return result!;
    }
}

internal sealed class WindowsInputDesktopException(string message) : InvalidOperationException(message);

internal interface IWindowsInputDesktopPlatform
{
    IntPtr OpenCurrentInputDesktop();

    bool SetCurrentThreadDesktop(IntPtr desktop);

    void CloseDesktop(IntPtr desktop);

    int GetLastError();
}

internal sealed class NativeWindowsInputDesktopPlatform : IWindowsInputDesktopPlatform
{
    private const uint AllowOtherAccountHook = 0x00000001;
    private const uint GenericAll = 0x10000000;

    public IntPtr OpenCurrentInputDesktop() =>
        NativeMethods.OpenInputDesktop(AllowOtherAccountHook, inherit: false, GenericAll);

    public bool SetCurrentThreadDesktop(IntPtr desktop) => NativeMethods.SetThreadDesktop(desktop);

    public void CloseDesktop(IntPtr desktop) => _ = NativeMethods.CloseDesktop(desktop);

    public int GetLastError() => Marshal.GetLastWin32Error();

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseDesktop(IntPtr desktop);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenInputDesktop(
            uint flags,
            [MarshalAs(UnmanagedType.Bool)] bool inherit,
            uint desiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetThreadDesktop(IntPtr desktop);
    }
}
