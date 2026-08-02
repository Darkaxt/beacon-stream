using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;
using Beacon.Platform.Windows.Sessions;

namespace Beacon.Platform.Windows.Tests.Sessions;

public sealed class WindowsSessionOwnedWorkTerminatorTests
{
    [Fact]
    public async Task TerminatesDistinctOwnedProcessesButNeverBeaconItself()
    {
        var activityApi = new FakeWindowsSessionActivityApi { CurrentProcessId = 999 };
        var terminator = new WindowsSessionOwnedWorkTerminator(activityApi);

        SessionOwnedWorkTerminationResult result = await terminator.TerminateAsync(
            CreateRecord(),
            new SessionActivitySnapshot(true, true, true, [])
            {
                OwnedProcessIds = [100, 200, 100, 999]
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal([100, 200], result.ProcessIds);
        Assert.Equal([100, 200], activityApi.TerminatedProcessIds);
    }

    [Fact]
    public async Task ReportsEveryProcessThatCouldNotBeTerminated()
    {
        var activityApi = new FakeWindowsSessionActivityApi();
        activityApi.FailedTerminationProcessIds.Add(200);
        var terminator = new WindowsSessionOwnedWorkTerminator(activityApi);

        SessionOwnedWorkTerminationResult result = await terminator.TerminateAsync(
            CreateRecord(),
            new SessionActivitySnapshot(true, true, false, [])
            {
                OwnedProcessIds = [100, 200]
            },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("200", result.Error, StringComparison.Ordinal);
    }

    private static SessionOwnershipRecord CreateRecord()
    {
        SessionPlan plan = new(
            "session-a",
            new ClientId("z-fold-7"),
            "steam:1",
            new PlannedDisplay("client-z-fold-7", 2560, 1600, 120, "virtual-primary", HdrPreference.Off, false, "sdr", "test"),
            new PlannedStream("h264", 2560, 1600, 60, 20, "lan-direct", "adaptive", "test", Guid.NewGuid(), "test"),
            new PlannedAudio("opus", 48_000, 2, 20_000, 96_000, "R2 test audio."));
        return new SessionOwnershipRecord(
            plan,
            new GameLaunchState(
                plan.SessionId,
                plan.AppId,
                "process",
                "game.exe",
                100,
                plan.Display.DisplayId,
                Started: true),
            DateTimeOffset.UtcNow);
    }
}
