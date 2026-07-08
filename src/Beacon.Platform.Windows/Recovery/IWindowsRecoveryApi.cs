using Beacon.Platform.Windows.Sessions;

namespace Beacon.Platform.Windows.Recovery;

public interface IWindowsRecoveryApi
{
    int CurrentProcessId { get; }

    IReadOnlyList<WindowsRecoveryWindow> EnumerateTopLevelWindows();

    void MoveWindow(IntPtr handle, int x, int y, int width, int height);

    void MinimizeWindow(IntPtr handle);

    void CloseWindow(IntPtr handle);

    void TerminateProcess(int processId);
}

public sealed record WindowsRecoveryWindow(
    IntPtr Handle,
    int ProcessId,
    string Title,
    WindowsRectangle Bounds,
    bool IsVisible);
