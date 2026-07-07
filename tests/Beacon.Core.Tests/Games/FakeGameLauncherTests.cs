using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;

namespace Beacon.Core.Tests.Games;

public sealed class FakeGameLauncherTests
{
    [Fact]
    public async Task LaunchAsyncRecordsLaunchIntentAndDisplayId()
    {
        var launcher = new FakeGameLauncher { NextProcessId = 1234 };
        GameDescriptor game = CreateGame();
        SessionPlan plan = CreatePlan(game);

        GameLaunchResult result = await launcher.LaunchAsync(
            new GameLaunchRequest(game, plan, "client-z-fold-7"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.State);
        Assert.Single(launcher.Requests);
        Assert.Equal("steam-shortcut:3767414131", launcher.Requests[0].Game.Id);
        Assert.Equal("client-z-fold-7", launcher.Requests[0].DisplayId);
        Assert.Equal(1234, result.State.ProcessId);
        Assert.Equal("steam-rungameid", result.State.LaunchType);
    }

    [Fact]
    public async Task LaunchAsyncReturnsDiagnosticError()
    {
        var launcher = new FakeGameLauncher { NextError = "Steam unavailable" };
        GameDescriptor game = CreateGame();
        SessionPlan plan = CreatePlan(game);

        GameLaunchResult result = await launcher.LaunchAsync(
            new GameLaunchRequest(game, plan, "client-z-fold-7"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.State);
        Assert.Equal("Steam unavailable", result.Error);
    }

    private static GameDescriptor CreateGame() =>
        new(
            "steam-shortcut:3767414131",
            "Dispatch",
            "steam-shortcut",
            new GameLaunchIntent("steam-rungameid", "steam://rungameid/16180920483166814208"),
            new GameArtwork(null, "none"),
            Installed: true,
            new GameProcessHints("Dispatch.exe", "D:/Games/Dispatch"));

    private static SessionPlan CreatePlan(GameDescriptor game) =>
        new(
            "z-fold-7-steam-shortcut:3767414131",
            new ClientId("z-fold-7"),
            game.Id,
            new PlannedDisplay("client-z-fold-7", 2560, 1600, 120, "virtual-primary", HdrPreference.Prefer, false, "sdr", "HDR unavailable."),
            new PlannedStream("av1", 120, 65, "lan-direct", "adaptive"));
}
