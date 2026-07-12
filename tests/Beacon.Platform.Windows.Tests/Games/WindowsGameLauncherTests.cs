using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;
using Beacon.Platform.Windows.Games;

namespace Beacon.Platform.Windows.Tests.Games;

public sealed class WindowsGameLauncherTests
{
    [Fact]
    public void CreateCommand_ForProcessLaunch_UsesExecutableAndWorkingDirectory()
    {
        GameDescriptor game = CreateGame("process", "D:/Games/Dispatch/Dispatch.exe", "D:/Games/Dispatch");

        WindowsGameLaunchCommand command = WindowsGameLauncher.CreateCommand(CreateRequest(game));

        Assert.Equal("D:/Games/Dispatch/Dispatch.exe", command.FileName);
        Assert.Null(command.Arguments);
        Assert.Equal("D:/Games/Dispatch", command.WorkingDirectory);
        Assert.False(command.UseShellExecute);
    }

    [Theory]
    [InlineData("steam-app", "steam://run/1086940")]
    [InlineData("steam-rungameid", "steam://rungameid/16180920483166814208")]
    public void CreateCommand_ForSteamLaunch_UsesShellExecuteUri(string type, string commandText)
    {
        GameDescriptor game = CreateGame(type, commandText, null);

        WindowsGameLaunchCommand command = WindowsGameLauncher.CreateCommand(CreateRequest(game));

        Assert.Equal(commandText, command.FileName);
        Assert.Null(command.Arguments);
        Assert.Null(command.WorkingDirectory);
        Assert.True(command.UseShellExecute);
    }

    private static GameLaunchRequest CreateRequest(GameDescriptor game) =>
        new(game, CreatePlan(game), "client-z-fold-7");

    private static GameDescriptor CreateGame(string type, string command, string? workingDirectory) =>
        new(
            "steam-shortcut:3767414131",
            "Dispatch",
            "steam-shortcut",
            new GameLaunchIntent(type, command),
            new GameArtwork(null, "none"),
            Installed: true,
            new GameProcessHints("Dispatch.exe", workingDirectory));

    private static SessionPlan CreatePlan(GameDescriptor game) =>
        new(
            "z-fold-7-steam-shortcut:3767414131",
            new ClientId("z-fold-7"),
            game.Id,
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
}
