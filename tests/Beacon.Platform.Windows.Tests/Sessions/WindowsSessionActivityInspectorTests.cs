using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Sessions;
using Beacon.Platform.Windows.Tests.Displays;

namespace Beacon.Platform.Windows.Tests.Sessions;

public sealed class WindowsSessionActivityInspectorTests
{
    private static readonly DateTimeOffset SessionStart = new(2026, 7, 8, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InspectAsync_DetectsLaunchedProcessRunning()
    {
        var activityApi = new FakeWindowsSessionActivityApi();
        activityApi.RunningProcesses.Add(100);
        var inspector = CreateInspector(activityApi);

        SessionActivitySnapshot snapshot = await inspector.InspectAsync(CreateRecord(), CancellationToken.None);

        Assert.True(snapshot.LaunchedProcessRunning);
        Assert.Contains(snapshot.Reasons, reason => reason.Contains("Launched process 100", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InspectAsync_DetectsChildProcessRunning()
    {
        var activityApi = new FakeWindowsSessionActivityApi();
        activityApi.ParentByProcessId[200] = 100;
        activityApi.RunningProcesses.Add(200);
        var inspector = CreateInspector(activityApi);

        SessionActivitySnapshot snapshot = await inspector.InspectAsync(CreateRecord(), CancellationToken.None);

        Assert.True(snapshot.ChildProcessRunning);
    }

    [Fact]
    public async Task InspectAsync_DetectsChildWindowOnVirtualDisplay()
    {
        var activityApi = new FakeWindowsSessionActivityApi();
        activityApi.ParentByProcessId[200] = 100;
        activityApi.Windows.Add(new WindowsTopLevelWindow(
            200,
            "Child Window",
            new WindowsRectangle(100, 100, 800, 600),
            IsVisible: true));
        var inspector = CreateInspector(activityApi);

        SessionActivitySnapshot snapshot = await inspector.InspectAsync(CreateRecord(), CancellationToken.None);

        Assert.True(snapshot.OwnedWindowRemaining);
    }

    [Fact]
    public async Task InspectAsync_IgnoresUnrelatedOldWindowOnVirtualDisplay()
    {
        var activityApi = new FakeWindowsSessionActivityApi();
        activityApi.StartTimeByProcessId[300] = SessionStart.AddMinutes(-10);
        activityApi.Windows.Add(new WindowsTopLevelWindow(
            300,
            "Updater",
            new WindowsRectangle(100, 100, 800, 600),
            IsVisible: true));
        var inspector = CreateInspector(activityApi);

        SessionActivitySnapshot snapshot = await inspector.InspectAsync(CreateRecord(), CancellationToken.None);

        Assert.False(snapshot.OwnedWindowRemaining);
    }

    [Fact]
    public async Task InspectAsync_DetectsNewWindowOnVirtualDisplayAfterSessionStart()
    {
        var activityApi = new FakeWindowsSessionActivityApi();
        activityApi.StartTimeByProcessId[300] = SessionStart.AddMinutes(1);
        activityApi.Windows.Add(new WindowsTopLevelWindow(
            300,
            "New Game Window",
            new WindowsRectangle(100, 100, 800, 600),
            IsVisible: true));
        var inspector = CreateInspector(activityApi);

        SessionActivitySnapshot snapshot = await inspector.InspectAsync(CreateRecord(), CancellationToken.None);

        Assert.True(snapshot.OwnedWindowRemaining);
    }

    private static WindowsSessionActivityInspector CreateInspector(FakeWindowsSessionActivityApi activityApi)
    {
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                "physical-laptop-panel",
                "client-z-fold-7",
                2560,
                1600,
                120,
                virtualPrimary: true)
        };

        return new WindowsSessionActivityInspector(activityApi, displayApi);
    }

    private static SessionOwnershipRecord CreateRecord()
    {
        SessionPlan plan = new(
            "z-fold-7-steam-shortcut:3767414131",
            new ClientId("z-fold-7"),
            "steam-shortcut:3767414131",
            new PlannedDisplay("client-z-fold-7", 2560, 1600, 120, "virtual-primary", HdrPreference.Prefer, false, "sdr", "HDR unavailable."),
            new PlannedStream(
                "av1",
                120,
                65,
                "lan-direct",
                "adaptive",
                "Test benchmark evidence.",
                Guid.Parse("33acde60-b29f-4f03-b2b2-f51337bdb9a5"),
                "test-benchmark-revision"));

        var launchState = new GameLaunchState(
            plan.SessionId,
            plan.AppId,
            "steam-rungameid",
            "steam://rungameid/16180920483166814208",
            100,
            plan.Display.DisplayId,
            Started: true);

        return new SessionOwnershipRecord(plan, launchState, SessionStart);
    }
}
