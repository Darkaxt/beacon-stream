using Beacon.GameProbe;

namespace Beacon.GameProbe.Tests;

public sealed class GameProbeCommandLineTests
{
    [Fact]
    public void ParsesScanCommand()
    {
        GameProbeCommand command = GameProbeCommandLine.Parse(["scan", "--json"]);

        var scan = Assert.IsType<ScanGameProbeCommand>(command);
        Assert.True(scan.Json);
    }

    [Fact]
    public void ParsesExplicitProviderPaths()
    {
        GameProbeCommand command = GameProbeCommandLine.Parse(
            [
                "scan",
                "--steam-root",
                "D:/Steam",
                "--heroic-root",
                "C:/Users/test/AppData/Roaming/heroic",
                "--hydra-db",
                "C:/Users/test/AppData/Roaming/hydra/hydra.db"
            ]);

        var scan = Assert.IsType<ScanGameProbeCommand>(command);
        Assert.Equal("D:/Steam", scan.SteamRoot);
        Assert.Equal("C:/Users/test/AppData/Roaming/heroic", scan.HeroicRoot);
        Assert.Equal("C:/Users/test/AppData/Roaming/hydra/hydra.db", scan.HydraDatabasePath);
    }

    [Fact]
    public void ParsesSteamShortcutsCommand()
    {
        GameProbeCommand command = GameProbeCommandLine.Parse(["steam-shortcuts", "D:/Steam/userdata/0/config/shortcuts.vdf"]);

        var shortcuts = Assert.IsType<SteamShortcutsGameProbeCommand>(command);
        Assert.Equal("D:/Steam/userdata/0/config/shortcuts.vdf", shortcuts.Path);
    }
}
