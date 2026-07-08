using Beacon.Core.Recovery;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Sessions;

namespace Beacon.Platform.Windows.Recovery;

public sealed class WindowsRecoveryBackend(
    IWindowsDisplayApi displayApi,
    IWindowsRecoveryApi recoveryApi) : IRecoveryBackend
{
    public async Task<RecoveryActionResult> MoveWindowsBackAsync(bool minimize, CancellationToken cancellationToken)
    {
        DisplayTopologySnapshot topology = await displayApi.QueryTopologyAsync(cancellationToken);
        DisplayPathSnapshot? physical = SelectPhysicalTarget(topology);
        if (physical is null)
        {
            return RecoveryActionResult.Fail(
                "move-windows-back",
                "No active physical display was found for moving windows back.");
        }

        IReadOnlyList<WindowsRecoveryWindow> windows = SelectVirtualWindows(topology);
        int moved = 0;
        foreach (WindowsRecoveryWindow window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int width = Math.Max(1, Math.Min(window.Bounds.Width, physical.Width));
            int height = Math.Max(1, Math.Min(window.Bounds.Height, physical.Height));
            recoveryApi.MoveWindow(window.Handle, physical.X, physical.Y, width, height);
            if (minimize)
            {
                recoveryApi.MinimizeWindow(window.Handle);
            }

            moved++;
        }

        return RecoveryActionResult.Ok(
            "move-windows-back",
            moved,
            [$"Moved {moved} window(s) to {physical.DisplayId}. Minimize={minimize}."]);
    }

    public async Task<RecoveryActionResult> CloseVirtualWindowsAsync(CancellationToken cancellationToken)
    {
        DisplayTopologySnapshot topology = await displayApi.QueryTopologyAsync(cancellationToken);
        IReadOnlyList<WindowsRecoveryWindow> windows = SelectVirtualWindows(topology);
        foreach (WindowsRecoveryWindow window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            recoveryApi.CloseWindow(window.Handle);
        }

        return RecoveryActionResult.Ok(
            "close-virtual-windows",
            windows.Count,
            [$"Close requested for {windows.Count} virtual-display window(s)."]);
    }

    public async Task<RecoveryActionResult> TerminateVirtualProcessesAsync(CancellationToken cancellationToken)
    {
        DisplayTopologySnapshot topology = await displayApi.QueryTopologyAsync(cancellationToken);
        int currentProcessId = recoveryApi.CurrentProcessId;
        int[] processIds = SelectVirtualWindows(topology)
            .Select(window => window.ProcessId)
            .Where(processId => processId != currentProcessId)
            .Distinct()
            .ToArray();

        foreach (int processId in processIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            recoveryApi.TerminateProcess(processId);
        }

        return RecoveryActionResult.Ok(
            "terminate-virtual-processes",
            processIds.Length,
            [$"Terminated {processIds.Length} process(es) with virtual-display windows."]);
    }

    private IReadOnlyList<WindowsRecoveryWindow> SelectVirtualWindows(DisplayTopologySnapshot topology)
    {
        DisplayPathSnapshot[] virtualDisplays = topology.Paths
            .Where(path => path.Kind == DisplayPathKind.Virtual)
            .ToArray();

        if (virtualDisplays.Length == 0)
        {
            return [];
        }

        return recoveryApi.EnumerateTopLevelWindows()
            .Where(window =>
                window.IsVisible &&
                virtualDisplays.Any(display => Intersects(window.Bounds, display)))
            .ToArray();
    }

    private static DisplayPathSnapshot? SelectPhysicalTarget(DisplayTopologySnapshot topology) =>
        topology.Paths
            .Where(path => path.Kind == DisplayPathKind.Physical)
            .OrderByDescending(path => path.IsPrimary)
            .ThenBy(path => path.DisplayId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static bool Intersects(WindowsRectangle rectangle, DisplayPathSnapshot display) =>
        rectangle.Intersects(display.X, display.Y, display.Width, display.Height);
}
