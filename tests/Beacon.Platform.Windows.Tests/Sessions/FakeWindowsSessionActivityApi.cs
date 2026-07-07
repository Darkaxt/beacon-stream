using Beacon.Platform.Windows.Sessions;

namespace Beacon.Platform.Windows.Tests.Sessions;

internal sealed class FakeWindowsSessionActivityApi : IWindowsSessionActivityApi
{
    public HashSet<int> RunningProcesses { get; } = [];

    public Dictionary<int, int> ParentByProcessId { get; } = [];

    public Dictionary<int, DateTimeOffset> StartTimeByProcessId { get; } = [];

    public List<WindowsTopLevelWindow> Windows { get; } = [];

    public bool IsProcessRunning(int processId) =>
        RunningProcesses.Contains(processId);

    public IReadOnlyList<int> GetChildProcessIds(int processId) =>
        ParentByProcessId
            .Where(pair => pair.Value == processId)
            .Select(pair => pair.Key)
            .ToArray();

    public DateTimeOffset? GetProcessStartTime(int processId) =>
        StartTimeByProcessId.GetValueOrDefault(processId);

    public IReadOnlyList<WindowsTopLevelWindow> EnumerateTopLevelWindows() =>
        Windows;
}
