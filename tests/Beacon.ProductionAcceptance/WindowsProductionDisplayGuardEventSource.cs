using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Beacon.ProductionAcceptance;

internal sealed class WindowsProductionDisplayGuardEventSource : IProductionDisplayGuardEventSource
{
    private const uint WindowClose = 0x0010;
    private const uint WindowDestroy = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyF12 = 0x7B;
    private const uint NotifyForThisSession = 0;

    private readonly TaskCompletionSource<ProductionDisplayGuardTrigger> trigger =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource windowReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Process? acceptance;
    private readonly EventWaitHandle completionEvent;
    private readonly RegisteredWaitHandle completionRegistration;
    private readonly Thread messageThread;
    private readonly NativeMethods.WindowProcedure windowProcedure;
    private IntPtr windowHandle;
    private uint messageThreadId;
    private bool disposed;

    public WindowsProductionDisplayGuardEventSource(
        int acceptanceProcessId,
        string completionEventName)
    {
        completionEvent = EventWaitHandle.OpenExisting(completionEventName);
        completionRegistration = ThreadPool.RegisterWaitForSingleObject(
            completionEvent,
            static (state, _) =>
                ((WindowsProductionDisplayGuardEventSource)state!).Signal(
                    ProductionDisplayGuardTrigger.CompletionSignaled),
            this,
            Timeout.Infinite,
            executeOnlyOnce: true);

        try
        {
            acceptance = Process.GetProcessById(acceptanceProcessId);
            acceptance.EnableRaisingEvents = true;
            acceptance.Exited += OnAcceptanceExited;
            if (acceptance.HasExited)
            {
                Signal(ProductionDisplayGuardTrigger.AcceptanceExited);
            }
        }
        catch (ArgumentException)
        {
            acceptance = null;
            Signal(ProductionDisplayGuardTrigger.AcceptanceExited);
        }

        windowProcedure = WindowProcedure;
        messageThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "Beacon Gate 5 display guard events",
        };
        messageThread.Start();
        windowReady.Task.GetAwaiter().GetResult();
    }

    public bool AcceptanceRunning
    {
        get
        {
            try
            {
                return acceptance is not null && !acceptance.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public Task<ProductionDisplayGuardTrigger> WaitForTriggerAsync(CancellationToken cancellationToken) =>
        trigger.Task.WaitAsync(cancellationToken);

    public async Task TerminateAcceptanceAsync(CancellationToken cancellationToken)
    {
        if (!AcceptanceRunning || acceptance is null)
        {
            return;
        }

        acceptance.Kill(entireProcessTree: false);
        await acceptance.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;

        completionRegistration.Unregister(null);
        completionEvent.Dispose();
        if (acceptance is not null)
        {
            acceptance.Exited -= OnAcceptanceExited;
            acceptance.Dispose();
        }

        IntPtr handle = windowHandle;
        if (handle != IntPtr.Zero)
        {
            _ = NativeMethods.PostMessage(handle, WindowClose, IntPtr.Zero, IntPtr.Zero);
        }
        else if (messageThreadId != 0)
        {
            _ = NativeMethods.PostThreadMessage(messageThreadId, WindowClose, IntPtr.Zero, IntPtr.Zero);
        }
        if (messageThread.IsAlive && Environment.CurrentManagedThreadId != messageThread.ManagedThreadId)
        {
            messageThread.Join();
        }
    }

    private void OnAcceptanceExited(object? sender, EventArgs eventArgs) =>
        Signal(ProductionDisplayGuardTrigger.AcceptanceExited);

    private void Signal(ProductionDisplayGuardTrigger value) => trigger.TrySetResult(value);

    private void RunMessageLoop()
    {
        string className = $"BeaconGate5DisplayGuard-{Environment.ProcessId}";
        IntPtr instance = NativeMethods.GetModuleHandle(null);
        var windowClass = new NativeMethods.WindowClassEx
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.WindowClassEx>()),
            Instance = instance,
            WindowProcedure = windowProcedure,
            ClassName = className,
        };

        try
        {
            ushort atom = NativeMethods.RegisterClassEx(ref windowClass);
            if (atom == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the display guard window class.");
            }

            messageThreadId = NativeMethods.GetCurrentThreadId();
            windowHandle = NativeMethods.CreateWindowEx(
                0,
                className,
                "Beacon Gate 5 Display Guard",
                0,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);
            if (windowHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the display guard event window.");
            }
            if (!NativeMethods.WtsRegisterSessionNotification(windowHandle, NotifyForThisSession))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not arm display guard session notifications.");
            }
            if (!NativeMethods.RegisterHotKey(
                    windowHandle,
                    checked((int)ProductionDisplayGuardWindowsEvents.EmergencyHotKeyId),
                    ModControl | ModAlt | ModShift | ModNoRepeat,
                    VirtualKeyF12))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not register the Ctrl+Alt+Shift+F12 display recovery hotkey.");
            }

            windowReady.TrySetResult();
            while (NativeMethods.GetMessage(out NativeMethods.Message message, IntPtr.Zero, 0, 0) > 0)
            {
                _ = NativeMethods.TranslateMessage(ref message);
                _ = NativeMethods.DispatchMessage(ref message);
            }
        }
        catch (Exception error)
        {
            windowReady.TrySetException(error);
            trigger.TrySetException(error);
        }
        finally
        {
            if (windowHandle != IntPtr.Zero)
            {
                _ = NativeMethods.UnregisterHotKey(
                    windowHandle,
                    checked((int)ProductionDisplayGuardWindowsEvents.EmergencyHotKeyId));
                _ = NativeMethods.WtsUnRegisterSessionNotification(windowHandle);
                _ = NativeMethods.DestroyWindow(windowHandle);
                windowHandle = IntPtr.Zero;
            }
            _ = NativeMethods.UnregisterClass(className, instance);
        }
    }

    private IntPtr WindowProcedure(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
    {
        ProductionDisplayGuardTrigger? mapped = ProductionDisplayGuardWindowsEvents.Classify(
            message,
            unchecked((nuint)wParam));
        if (mapped is ProductionDisplayGuardTrigger value)
        {
            Signal(value);
            return IntPtr.Zero;
        }
        if (message == WindowClose)
        {
            _ = NativeMethods.DestroyWindow(handle);
            return IntPtr.Zero;
        }
        if (message == WindowDestroy)
        {
            NativeMethods.PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(handle, message, wParam, lParam);
    }

    private static class NativeMethods
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate IntPtr WindowProcedure(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WindowClassEx
        {
            internal uint Size;
            internal uint Style;
            internal WindowProcedure WindowProcedure;
            internal int ClassExtra;
            internal int WindowExtra;
            internal IntPtr Instance;
            internal IntPtr Icon;
            internal IntPtr Cursor;
            internal IntPtr Background;
            internal string? MenuName;
            internal string ClassName;
            internal IntPtr SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Message
        {
            internal IntPtr Handle;
            internal uint Value;
            internal UIntPtr WParam;
            internal IntPtr LParam;
            internal uint Time;
            internal Point Point;
            internal uint Private;
        }

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

        [DllImport("user32.dll", EntryPoint = "UnregisterClassW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterClass(string className, IntPtr instance);

        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateWindowEx(
            uint extendedStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(IntPtr handle);

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
        internal static extern IntPtr DefWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
        internal static extern int GetMessage(out Message message, IntPtr handle, uint minimum, uint maximum);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
        internal static extern IntPtr DispatchMessage(ref Message message);

        [DllImport("user32.dll")]
        internal static extern void PostQuitMessage(int exitCode);

        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr handle, int id);

        [DllImport("wtsapi32.dll", EntryPoint = "WTSRegisterSessionNotification", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WtsRegisterSessionNotification(IntPtr handle, uint flags);

        [DllImport("wtsapi32.dll", EntryPoint = "WTSUnRegisterSessionNotification", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WtsUnRegisterSessionNotification(IntPtr handle);
    }
}
