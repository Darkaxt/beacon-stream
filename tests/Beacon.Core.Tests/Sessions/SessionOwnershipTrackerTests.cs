using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;

namespace Beacon.Core.Tests.Sessions;

public sealed class SessionOwnershipTrackerTests
{
    [Fact]
    public async Task LaunchedProcessRunningBlocksCleanup()
    {
        var inspector = new FakeSessionActivityInspector();
        SessionOwnershipTracker tracker = CreateTracker(inspector, out SessionPlan plan);
        inspector.SetActivity(plan.SessionId, new SessionActivitySnapshot(true, false, false, [])
        {
            OwnedProcessIds = [1234]
        });

        SessionOwnershipSnapshot? snapshot = await tracker.GetSnapshotAsync(plan.SessionId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.HasOwnedWork);
        Assert.True(snapshot.LaunchedProcessRunning);
        Assert.Contains("Launched process", snapshot.Reasons[0]);
        Assert.Equal(plan.Display.DisplayId, snapshot.DisplayId);
        Assert.Equal([1234], snapshot.OwnedProcessIds);
    }

    [Fact]
    public async Task ChildProcessRunningBlocksCleanup()
    {
        var inspector = new FakeSessionActivityInspector();
        SessionOwnershipTracker tracker = CreateTracker(inspector, out SessionPlan plan);
        inspector.SetActivity(plan.SessionId, new SessionActivitySnapshot(false, true, false, []));

        SessionOwnershipSnapshot? snapshot = await tracker.GetSnapshotAsync(plan.SessionId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.HasOwnedWork);
        Assert.True(snapshot.ChildProcessRunning);
    }

    [Fact]
    public async Task OwnedTopLevelWindowBlocksCleanup()
    {
        var inspector = new FakeSessionActivityInspector();
        SessionOwnershipTracker tracker = CreateTracker(inspector, out SessionPlan plan);
        inspector.SetActivity(plan.SessionId, new SessionActivitySnapshot(false, false, true, []));

        SessionOwnershipSnapshot? snapshot = await tracker.GetSnapshotAsync(plan.SessionId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.HasOwnedWork);
        Assert.True(snapshot.OwnedWindowRemaining);
    }

    [Fact]
    public async Task UnrelatedActivityDoesNotBlockCleanup()
    {
        var inspector = new FakeSessionActivityInspector();
        SessionOwnershipTracker tracker = CreateTracker(inspector, out SessionPlan plan);
        inspector.SetActivity("other-session", new SessionActivitySnapshot(true, true, true, ["unrelated"]));

        SessionOwnershipSnapshot? snapshot = await tracker.GetSnapshotAsync(plan.SessionId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.False(snapshot.HasOwnedWork);
        Assert.Empty(snapshot.Reasons);
    }

    [Fact]
    public async Task EmptyOwnedSnapshotAllowsCleanup()
    {
        var inspector = new FakeSessionActivityInspector();
        SessionOwnershipTracker tracker = CreateTracker(inspector, out SessionPlan plan);

        SessionOwnershipSnapshot? snapshot = await tracker.GetSnapshotAsync(plan.SessionId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.False(snapshot.HasOwnedWork);
        Assert.False(snapshot.LaunchedProcessRunning);
        Assert.False(snapshot.ChildProcessRunning);
        Assert.False(snapshot.OwnedWindowRemaining);
    }

    [Fact]
    public async Task TerminateOwnedWorkUsesInspectedProcessesAndClearsVerifiedRecord()
    {
        var inspector = new FakeSessionActivityInspector();
        var terminator = new RecordingOwnedWorkTerminator(inspector);
        SessionOwnershipTracker tracker = CreateTracker(inspector, terminator, out SessionPlan plan);
        inspector.SetActivity(
            plan.SessionId,
            new SessionActivitySnapshot(true, true, true, [])
            {
                OwnedProcessIds = [1234, 1235, 1236]
            });

        SessionOwnedWorkTerminationResult result = await tracker.TerminateOwnedWorkAsync(
            plan.SessionId,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal([1234, 1235, 1236], result.ProcessIds);
        Assert.Equal([1234, 1235, 1236], Assert.Single(terminator.Requests));
        Assert.Null(await tracker.GetSnapshotAsync(plan.SessionId, CancellationToken.None));
    }

    [Fact]
    public async Task UnobservableShellLaunchRemainsTrackedForLaterCleanup()
    {
        var inspector = new FakeSessionActivityInspector();
        SessionPlan plan = CreatePlan();
        var tracker = new SessionOwnershipTracker(
            inspector,
            new RecordingOwnedWorkTerminator(inspector));
        await tracker.RecordLaunchAsync(
            plan,
            new GameLaunchState(
                plan.SessionId,
                plan.AppId,
                "steam-rungameid",
                "steam://rungameid/16180920483166814208",
                ProcessId: null,
                plan.Display.DisplayId,
                Started: true),
            CancellationToken.None);

        SessionOwnedWorkTerminationResult result = await tracker.TerminateOwnedWorkAsync(
            plan.SessionId,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not observable", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await tracker.GetSnapshotAsync(plan.SessionId, CancellationToken.None));
    }

    private static SessionOwnershipTracker CreateTracker(FakeSessionActivityInspector inspector, out SessionPlan plan)
    {
        return CreateTracker(
            inspector,
            new RecordingOwnedWorkTerminator(inspector),
            out plan);
    }

    private static SessionOwnershipTracker CreateTracker(
        FakeSessionActivityInspector inspector,
        ISessionOwnedWorkTerminator terminator,
        out SessionPlan plan)
    {
        plan = CreatePlan();
        var launchState = new GameLaunchState(
            plan.SessionId,
            plan.AppId,
            "steam-rungameid",
            "steam://rungameid/16180920483166814208",
            1234,
            plan.Display.DisplayId,
            Started: true);

        var tracker = new SessionOwnershipTracker(inspector, terminator);
        tracker.RecordLaunchAsync(plan, launchState, CancellationToken.None).GetAwaiter().GetResult();
        return tracker;
    }

    private sealed class RecordingOwnedWorkTerminator(FakeSessionActivityInspector inspector)
        : ISessionOwnedWorkTerminator
    {
        public List<IReadOnlyList<int>> Requests { get; } = [];

        public Task<SessionOwnedWorkTerminationResult> TerminateAsync(
            SessionOwnershipRecord record,
            SessionActivitySnapshot activity,
            CancellationToken cancellationToken)
        {
            Requests.Add(activity.OwnedProcessIds);
            inspector.ClearActivity(record.Plan.SessionId);
            return Task.FromResult(SessionOwnedWorkTerminationResult.Ok(activity.OwnedProcessIds));
        }
    }

    private static SessionPlan CreatePlan() =>
        new(
            "z-fold-7-steam-shortcut:3767414131",
            new ClientId("z-fold-7"),
            "steam-shortcut:3767414131",
            new PlannedDisplay("client-z-fold-7", 2560, 1600, 120, "virtual-primary", HdrPreference.Prefer, false, "sdr", "HDR unavailable."),
            new PlannedStream(
                "av1",
                2560,
                1600,
                120,
                65,
                "lan-direct",
                "adaptive",
                "Test benchmark evidence.",
                Guid.Parse("33acde60-b29f-4f03-b2b2-f51337bdb9a5"),
                "test-benchmark-revision"));
}
