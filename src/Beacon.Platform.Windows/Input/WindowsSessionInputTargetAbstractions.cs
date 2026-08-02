using Beacon.Core.Input;

namespace Beacon.Platform.Windows.Input;

public sealed record WindowsSessionInputTargetResult(
    bool Success,
    int? ProcessId,
    nint? WindowHandle,
    string? Error)
{
    public static WindowsSessionInputTargetResult Activated(int processId, nint windowHandle) =>
        new(true, processId, windowHandle, null);

    public static WindowsSessionInputTargetResult Fail(string error) =>
        new(false, null, null, error);
}

public interface IWindowsSessionInputTargetActivator
{
    Task<WindowsSessionInputTargetResult> ActivateAsync(
        ClientInputBatch batch,
        CancellationToken cancellationToken);
}
