using Beacon.Core.Games;

namespace Beacon.Core.Tests.Games;

public sealed class GameDescriptorTests
{
    [Fact]
    public void GameDescriptorCarriesLaunchArtworkInstalledAndHintsWithoutDisplayPolicy()
    {
        var game = new GameDescriptor(
            Id: "steam-shortcut:4261190003",
            Title: "Stranger of Sword City",
            Source: "steam-shortcut",
            Launch: new GameLaunchIntent("steam-rungameid", "steam://rungameid/18301671704960696320"),
            Artwork: new GameArtwork("C:/ProgramData/BeaconStream/artwork/stranger.png", "steamgriddb"),
            Installed: true,
            ProcessHints: new GameProcessHints("SoSC.exe", "D:/Games/Saviors of Sapphire Wings Stranger of Sword City Revisited/SoSC"));

        Assert.Equal("steam-rungameid", game.Launch.Type);
        Assert.Equal("steamgriddb", game.Artwork.Source);
        Assert.True(game.Installed);
        Assert.Equal("SoSC.exe", game.ProcessHints.ExecutableName);
        Assert.DoesNotContain(
            game.GetType().GetProperties().Select(property => property.Name),
            name => name.Contains("Display", StringComparison.OrdinalIgnoreCase));
    }
}
