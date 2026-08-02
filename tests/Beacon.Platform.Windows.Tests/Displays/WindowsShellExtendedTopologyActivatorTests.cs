using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsShellExtendedTopologyActivatorTests
{
    [Fact]
    public void RequestsFixedDisplaySwitchExtendCommandThroughExplorer()
    {
        var executor = new RecordingExplorerShellExecutor();
        var activator = new WindowsShellExtendedTopologyActivator(executor);

        DisplayApiResult result = activator.Apply();

        Assert.True(result.Success);
        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "DisplaySwitch.exe"),
            executor.FileName);
        Assert.Equal("/extend", executor.Arguments);
    }

    [Fact]
    public void ExplorerLaunchFailureIsReturnedToTopologyGate()
    {
        var activator = new WindowsShellExtendedTopologyActivator(
            new RecordingExplorerShellExecutor
            {
                Result = DisplayApiResult.Fail("Explorer shell unavailable.")
            });

        DisplayApiResult result = activator.Apply();

        Assert.False(result.Success);
        Assert.Contains("Explorer", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitHostAgentPathLaunchesItsFixedUserTopologyHelperThroughExplorer()
    {
        var executor = new RecordingExplorerShellExecutor();
        const string hostAgent = @"C:\Program Files\Beacon Stream\Beacon.HostAgent.exe";
        var activator = new WindowsShellExtendedTopologyActivator(
            executor,
            userTopologyHelperExecutable: hostAgent);

        DisplayApiResult result = activator.Apply();

        Assert.True(result.Success);
        Assert.Equal(hostAgent, executor.FileName);
        Assert.Equal(
            WindowsUserDisplayTopologyTransition.CommandArgument,
            executor.Arguments);
    }

    [Fact]
    public void ExplorerFallbackSelectsOldestProcessFromCurrentSession()
    {
        uint? selected = WindowsExplorerShellExecutor.SelectExplorerProcessId(
            currentSessionId: 7,
            [
                new(301, SessionId: 8, StartTimeUtc: new DateTime(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc)),
                new(302, SessionId: 7, StartTimeUtc: new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc)),
                new(303, SessionId: 7, StartTimeUtc: new DateTime(2026, 8, 2, 7, 0, 0, DateTimeKind.Utc))
            ]);

        Assert.Equal((uint)303, selected);
    }

    [Fact]
    public void ExplorerFallbackRejectsAnotherSession()
    {
        uint? selected = WindowsExplorerShellExecutor.SelectExplorerProcessId(
            currentSessionId: 7,
            [new(301, SessionId: 8, StartTimeUtc: DateTime.UnixEpoch)]);

        Assert.Null(selected);
    }

    [Fact]
    public void ExplorerChildUsesTheDefaultInteractiveDesktop()
    {
        Assert.Equal(
            @"winsta0\default",
            WindowsExplorerShellExecutor.InteractiveDesktopName);
    }

    [Fact]
    public void FailedSignedHelperFallsBackToFixedDisplaySwitchCommand()
    {
        var executor = new RecordingExplorerShellExecutor();
        executor.Results.Enqueue(DisplayApiResult.Fail("SetDisplayConfig failed."));
        executor.Results.Enqueue(DisplayApiResult.Ok());
        const string hostAgent = @"C:\Program Files\Beacon Stream\Beacon.HostAgent.exe";
        var activator = new WindowsShellExtendedTopologyActivator(
            executor,
            userTopologyHelperExecutable: hostAgent);

        DisplayApiResult result = activator.Apply();

        Assert.True(result.Success);
        Assert.Collection(
            executor.Calls,
            call =>
            {
                Assert.Equal(hostAgent, call.FileName);
                Assert.Equal(WindowsUserDisplayTopologyTransition.CommandArgument, call.Arguments);
            },
            call =>
            {
                Assert.Equal(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                        "System32",
                        "DisplaySwitch.exe"),
                    call.FileName);
                Assert.Equal("/extend", call.Arguments);
            });
    }

    private sealed class RecordingExplorerShellExecutor : IWindowsExplorerShellExecutor
    {
        public DisplayApiResult Result { get; init; } = DisplayApiResult.Ok();

        public Queue<DisplayApiResult> Results { get; } = new();

        public List<(string FileName, string Arguments)> Calls { get; } = [];

        public string? FileName { get; private set; }

        public string? Arguments { get; private set; }

        public DisplayApiResult Execute(string fileName, string arguments)
        {
            FileName = fileName;
            Arguments = arguments;
            Calls.Add((fileName, arguments));
            return Results.TryDequeue(out DisplayApiResult? result) ? result : Result;
        }
    }
}
