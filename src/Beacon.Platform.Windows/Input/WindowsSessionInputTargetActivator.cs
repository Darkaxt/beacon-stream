using Beacon.Core.Input;
using Beacon.Core.Sessions;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Sessions;

namespace Beacon.Platform.Windows.Input;

public sealed class WindowsSessionInputTargetActivator(
    ISessionOwnershipTracker ownership,
    IWindowsDisplayApi displayApi,
    IWindowsSessionActivityApi activityApi) : IWindowsSessionInputTargetActivator
{
    public async Task<WindowsSessionInputTargetResult> ActivateAsync(
        ClientInputBatch batch,
        CancellationToken cancellationToken)
    {
        SessionOwnershipSnapshot? snapshot = await ownership.GetSnapshotAsync(
            batch.SessionId,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null ||
            !string.Equals(snapshot.ClientId.Value, batch.ClientId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(snapshot.DisplayId, batch.DisplayId, StringComparison.OrdinalIgnoreCase))
        {
            return WindowsSessionInputTargetResult.Fail(
                "No verified session ownership record matches the input target.");
        }

        DisplayTopologySnapshot topology = await displayApi.QueryTopologyAsync(cancellationToken)
            .ConfigureAwait(false);
        DisplayPathSnapshot? display = topology.Paths.FirstOrDefault(path =>
            string.Equals(path.DisplayId, batch.DisplayId, StringComparison.OrdinalIgnoreCase));
        if (display is null)
        {
            return WindowsSessionInputTargetResult.Fail(
                "The session-owned input display is not active.");
        }

        HashSet<int> ownedProcessIds = snapshot.OwnedProcessIds
            .Where(processId => processId > 0)
            .ToHashSet();
        WindowsTopLevelWindow? target = activityApi.EnumerateTopLevelWindows()
            .Where(window =>
                window.Handle != 0 &&
                window.IsVisible &&
                ownedProcessIds.Contains(window.ProcessId) &&
                window.Bounds.Intersects(display.X, display.Y, display.Width, display.Height))
            .OrderByDescending(window => window.ProcessId == snapshot.LaunchedProcessId)
            .ThenByDescending(window => IntersectionArea(window.Bounds, display))
            .ThenBy(window => window.ProcessId)
            .FirstOrDefault();
        if (target is null)
        {
            return WindowsSessionInputTargetResult.Fail(
                "No verified session-owned window is active on the target display.");
        }

        WindowsTopLevelWindowActivationResult activation =
            activityApi.ActivateTopLevelWindow(target.Handle);
        return activation.Success
            ? WindowsSessionInputTargetResult.Activated(target.ProcessId, target.Handle)
            : WindowsSessionInputTargetResult.Fail(
                activation.Error ?? "The verified session-owned window could not be activated.");
    }

    private static long IntersectionArea(
        WindowsRectangle window,
        DisplayPathSnapshot display)
    {
        int left = Math.Max(window.X, display.X);
        int top = Math.Max(window.Y, display.Y);
        int right = Math.Min(window.X + window.Width, display.X + display.Width);
        int bottom = Math.Min(window.Y + window.Height, display.Y + display.Height);
        return right > left && bottom > top
            ? checked((long)(right - left) * (bottom - top))
            : 0;
    }
}
