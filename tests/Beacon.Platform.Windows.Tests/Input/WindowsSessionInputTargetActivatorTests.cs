using Beacon.Core.Clients;
using Beacon.Core.Input;
using Beacon.Core.Sessions;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Input;
using Beacon.Platform.Windows.Sessions;
using Beacon.Platform.Windows.Tests.Displays;
using Beacon.Platform.Windows.Tests.Sessions;

namespace Beacon.Platform.Windows.Tests.Input;

public sealed class WindowsSessionInputTargetActivatorTests
{
    [Fact]
    public async Task ActivatesLaunchedSessionWindowOnRequestedDisplay()
    {
        var snapshot = new SessionOwnershipSnapshot(
            "session-1",
            new ClientId("z-fold-7"),
            "game-1",
            LaunchedProcessId: 100,
            LaunchedProcessRunning: true,
            ChildProcessRunning: true,
            OwnedWindowRemaining: true,
            Reasons: [])
        {
            DisplayId = "client-z-fold-7",
            OwnedProcessIds = [100, 200]
        };
        var ownership = new FakeSessionOwnershipTracker(snapshot);
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = new DisplayTopologySnapshot(
                [
                    new DisplayPathSnapshot(
                        "physical", DisplayPathKind.Physical, 2560, 1600, 240, false, 0, 0),
                    new DisplayPathSnapshot(
                        "client-z-fold-7", DisplayPathKind.Virtual, 2560, 1600, 120, true, 2560, 0)
                ],
                IsMirrorMode: false)
        };
        var windows = new FakeWindowsSessionActivityApi();
        windows.Windows.Add(new WindowsTopLevelWindow(
            999,
            "Unrelated foreground window",
            new WindowsRectangle(0, 0, 1200, 800),
            IsVisible: true)
        {
            Handle = 10
        });
        windows.Windows.Add(new WindowsTopLevelWindow(
            200,
            "Owned child",
            new WindowsRectangle(2560, 0, 1600, 900),
            IsVisible: true)
        {
            Handle = 20
        });
        windows.Windows.Add(new WindowsTopLevelWindow(
            100,
            "Launched game",
            new WindowsRectangle(2560, 0, 2560, 1600),
            IsVisible: true)
        {
            Handle = 30
        });
        var activator = new WindowsSessionInputTargetActivator(ownership, displayApi, windows);

        WindowsSessionInputTargetResult result = await activator.ActivateAsync(
            Batch(),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(100, result.ProcessId);
        Assert.Equal((nint)30, result.WindowHandle);
        Assert.Equal([(nint)30], windows.ActivatedWindowHandles);
    }

    [Fact]
    public async Task RejectsInputWhenNoOwnedWindowIntersectsTheSessionDisplay()
    {
        var snapshot = new SessionOwnershipSnapshot(
            "session-1",
            new ClientId("z-fold-7"),
            "game-1",
            LaunchedProcessId: 100,
            LaunchedProcessRunning: true,
            ChildProcessRunning: false,
            OwnedWindowRemaining: false,
            Reasons: [])
        {
            DisplayId = "client-z-fold-7",
            OwnedProcessIds = [100]
        };
        var ownership = new FakeSessionOwnershipTracker(snapshot);
        var displayApi = new FakeWindowsDisplayApi
        {
            CurrentTopology = DisplayTopologySnapshot.Extended(
                "physical",
                "client-z-fold-7",
                2560,
                1600,
                120,
                virtualPrimary: true)
        };
        var windows = new FakeWindowsSessionActivityApi();
        windows.Windows.Add(new WindowsTopLevelWindow(
            999,
            "Unrelated",
            new WindowsRectangle(0, 0, 800, 600),
            IsVisible: true)
        {
            Handle = 10
        });
        var activator = new WindowsSessionInputTargetActivator(ownership, displayApi, windows);

        WindowsSessionInputTargetResult result = await activator.ActivateAsync(
            Batch(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("session-owned window", result.Error, StringComparison.Ordinal);
        Assert.Empty(windows.ActivatedWindowHandles);
    }

    private static ClientInputBatch Batch() => new(
        "z-fold-7",
        "session-1",
        "client-z-fold-7",
        1,
        [ClientInputEvent.StreamKeyboard(0x58, pressed: true)]);

    private sealed class FakeSessionOwnershipTracker(SessionOwnershipSnapshot snapshot)
        : ISessionOwnershipTracker
    {
        public Task RecordLaunchAsync(
            SessionPlan plan,
            Beacon.Core.Games.GameLaunchState launchState,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<SessionOwnershipSnapshot?> GetSnapshotAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<SessionOwnershipSnapshot?>(
                string.Equals(sessionId, snapshot.SessionId, StringComparison.Ordinal)
                    ? snapshot
                    : null);

        public Task<IReadOnlyList<SessionOwnershipSnapshot>> GetSnapshotsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionOwnershipSnapshot>>([snapshot]);

        public Task<SessionOwnedWorkTerminationResult> TerminateOwnedWorkAsync(
            string sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(SessionOwnedWorkTerminationResult.Ok([]));

        public Task ClearAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
