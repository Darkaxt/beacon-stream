using Beacon.Platform.Windows.Sessions;

namespace Beacon.Platform.Windows.Tests.Sessions;

internal sealed class FakeWindowsSessionActivityApi : IWindowsSessionActivityApi
{
    public int CurrentProcessId { get; set; } = 999;

    public HashSet<int> RunningProcesses { get; } = [];

    public Dictionary<int, int> ParentByProcessId { get; } = [];

    public Dictionary<int, DateTimeOffset> StartTimeByProcessId { get; } = [];

    public List<WindowsTopLevelWindow> Windows { get; } = [];

    public List<int> TerminatedProcessIds { get; } = [];

    public List<nint> ActivatedWindowHandles { get; } = [];

    public WindowsTopLevelWindowActivationResult ActivationResult { get; set; } =
        WindowsTopLevelWindowActivationResult.Activated();

    public HashSet<int> FailedTerminationProcessIds { get; } = [];

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

    public WindowsTopLevelWindowActivationResult ActivateTopLevelWindow(nint windowHandle)
    {
        ActivatedWindowHandles.Add(windowHandle);
        return ActivationResult;
    }

    public Task<bool> TerminateProcessAsync(int processId, CancellationToken cancellationToken)
    {
        TerminatedProcessIds.Add(processId);
        RunningProcesses.Remove(processId);
        Windows.RemoveAll(window => window.ProcessId == processId);
        return Task.FromResult(!FailedTerminationProcessIds.Contains(processId));
    }
}
