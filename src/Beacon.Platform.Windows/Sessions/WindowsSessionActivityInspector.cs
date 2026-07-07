using Beacon.Core.Sessions;
using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Sessions;

public sealed class WindowsSessionActivityInspector(
    IWindowsSessionActivityApi activityApi,
    IWindowsDisplayApi displayApi) : ISessionActivityInspector
{
    public async Task<SessionActivitySnapshot> InspectAsync(SessionOwnershipRecord record, CancellationToken cancellationToken)
    {
        bool launchedProcessRunning = record.LaunchState.ProcessId is int processId &&
            activityApi.IsProcessRunning(processId);

        IReadOnlyList<int> childProcessIds = record.LaunchState.ProcessId is int parentProcessId
            ? activityApi.GetChildProcessIds(parentProcessId)
            : [];
        var childProcessIdSet = childProcessIds.ToHashSet();
        bool childProcessRunning = childProcessIds.Any(activityApi.IsProcessRunning);

        DisplayTopologySnapshot topology = await displayApi.QueryTopologyAsync(cancellationToken);
        DisplayPathSnapshot? sessionDisplay = topology.Paths.FirstOrDefault(path =>
            path.DisplayId.Equals(record.Plan.Display.DisplayId, StringComparison.OrdinalIgnoreCase));

        bool ownedWindowRemaining = sessionDisplay is not null &&
            activityApi.EnumerateTopLevelWindows().Any(window =>
                window.IsVisible &&
                window.Bounds.Intersects(sessionDisplay.X, sessionDisplay.Y, sessionDisplay.Width, sessionDisplay.Height) &&
                IsOwnedWindow(record, childProcessIdSet, window));

        return new SessionActivitySnapshot(
            launchedProcessRunning,
            childProcessRunning,
            ownedWindowRemaining,
            CreateReasons(record, launchedProcessRunning, childProcessRunning, ownedWindowRemaining));
    }

    private bool IsOwnedWindow(
        SessionOwnershipRecord record,
        HashSet<int> childProcessIds,
        WindowsTopLevelWindow window)
    {
        if (record.LaunchState.ProcessId == window.ProcessId)
        {
            return true;
        }

        if (childProcessIds.Contains(window.ProcessId))
        {
            return true;
        }

        DateTimeOffset? processStart = activityApi.GetProcessStartTime(window.ProcessId);
        return processStart is not null && processStart >= record.StartedAt;
    }

    private static IReadOnlyList<string> CreateReasons(
        SessionOwnershipRecord record,
        bool launchedProcessRunning,
        bool childProcessRunning,
        bool ownedWindowRemaining)
    {
        var reasons = new List<string>();
        if (launchedProcessRunning && record.LaunchState.ProcessId is int processId)
        {
            reasons.Add($"Launched process {processId} is still running.");
        }

        if (childProcessRunning)
        {
            reasons.Add("Child process is still running.");
        }

        if (ownedWindowRemaining)
        {
            reasons.Add($"Owned window remains on display {record.Plan.Display.DisplayId}.");
        }

        return reasons;
    }
}
