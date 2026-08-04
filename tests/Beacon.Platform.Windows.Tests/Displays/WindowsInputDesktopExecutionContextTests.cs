using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsInputDesktopExecutionContextTests
{
    [Fact]
    public void InvokeBindsFreshThreadToCurrentInputDesktop()
    {
        var platform = new FakeWindowsInputDesktopPlatform();
        var context = new WindowsInputDesktopExecutionContext(platform);
        int callerThread = Environment.CurrentManagedThreadId;

        int operationThread = context.Invoke(() => Environment.CurrentManagedThreadId);

        Assert.NotEqual(callerThread, operationThread);
        Assert.Equal(operationThread, platform.OpenThreadId);
        Assert.Equal(operationThread, platform.SetThreadId);
        Assert.Equal(operationThread, platform.CloseThreadId);
        Assert.Equal(new IntPtr(42), platform.SetDesktop);
        Assert.Equal(new IntPtr(42), platform.ClosedDesktop);
    }

    [Fact]
    public void InvokeDoesNotRunOperationWhenDesktopBindingFails()
    {
        var platform = new FakeWindowsInputDesktopPlatform
        {
            SetResult = false,
            LastError = 5
        };
        var context = new WindowsInputDesktopExecutionContext(platform);
        bool executed = false;

        WindowsInputDesktopException error = Assert.Throws<WindowsInputDesktopException>(
            () => context.Invoke(() => executed = true));

        Assert.False(executed);
        Assert.Contains("Win32=5", error.Message, StringComparison.Ordinal);
        Assert.Equal(new IntPtr(42), platform.ClosedDesktop);
    }

    [Fact]
    public void InvokePropagatesOperationFailureAfterClosingDesktop()
    {
        var platform = new FakeWindowsInputDesktopPlatform();
        var context = new WindowsInputDesktopExecutionContext(platform);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => context.Invoke<int>(() => throw new InvalidOperationException("expected")));

        Assert.Equal("expected", error.Message);
        Assert.Equal(new IntPtr(42), platform.ClosedDesktop);
    }

    private sealed class FakeWindowsInputDesktopPlatform : IWindowsInputDesktopPlatform
    {
        public bool SetResult { get; init; } = true;

        public int LastError { get; init; }

        public int OpenThreadId { get; private set; }

        public int SetThreadId { get; private set; }

        public int CloseThreadId { get; private set; }

        public IntPtr SetDesktop { get; private set; }

        public IntPtr ClosedDesktop { get; private set; }

        public IntPtr OpenCurrentInputDesktop()
        {
            OpenThreadId = Environment.CurrentManagedThreadId;
            return new IntPtr(42);
        }

        public bool SetCurrentThreadDesktop(IntPtr desktop)
        {
            SetThreadId = Environment.CurrentManagedThreadId;
            SetDesktop = desktop;
            return SetResult;
        }

        public void CloseDesktop(IntPtr desktop)
        {
            CloseThreadId = Environment.CurrentManagedThreadId;
            ClosedDesktop = desktop;
        }

        public int GetLastError() => LastError;
    }
}
