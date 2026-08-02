namespace Beacon.Platform.Windows.Sessions;

public interface IWindowsSessionActivityApi
{
    int CurrentProcessId { get; }

    bool IsProcessRunning(int processId);

    IReadOnlyList<int> GetChildProcessIds(int processId);

    DateTimeOffset? GetProcessStartTime(int processId);

    IReadOnlyList<WindowsTopLevelWindow> EnumerateTopLevelWindows();

    WindowsTopLevelWindowActivationResult ActivateTopLevelWindow(nint windowHandle);

    Task<bool> TerminateProcessAsync(int processId, CancellationToken cancellationToken);
}

public sealed record WindowsTopLevelWindow(
    int ProcessId,
    string Title,
    WindowsRectangle Bounds,
    bool IsVisible)
{
    public nint Handle { get; init; }
}

public sealed record WindowsTopLevelWindowActivationResult(bool Success, string? Error)
{
    public static WindowsTopLevelWindowActivationResult Activated() => new(true, null);

    public static WindowsTopLevelWindowActivationResult Fail(string error) => new(false, error);
}

public sealed record WindowsRectangle(int X, int Y, int Width, int Height)
{
    public bool Intersects(int x, int y, int width, int height) =>
        Width > 0 &&
        Height > 0 &&
        width > 0 &&
        height > 0 &&
        X < x + width &&
        X + Width > x &&
        Y < y + height &&
        Y + Height > y;
}
