using Beacon.Core.Games;
using Beacon.Core.Games.Heroic;

namespace Beacon.Core.Tests.Games.Heroic;

public sealed class HeroicGameLibraryProviderTests
{
    [Fact]
    public async Task ReadsGogInstalledJsonAndGamesConfig()
    {
        using HeroicFixture fixture = HeroicFixture.Create();
        fixture.WriteGogInstalled(
            """
            {
              "installed": [
                {
                  "appName": "1453375253",
                  "title": "Stardew Valley",
                  "install_path": "D:/Games/Heroic/Stardew Valley",
                  "executable": "Stardew Valley.exe",
                  "version": "1.6.14"
                }
              ]
            }
            """);
        fixture.WriteGameConfig("1453375253.json", """{"appName":"1453375253","title":"Stardew Valley"}""");

        IGameLibraryProvider provider = new HeroicGameLibraryProvider(fixture.Root);
        GameLibrarySnapshot snapshot = await provider.ScanAsync(CancellationToken.None);

        GameDescriptor game = Assert.Single(snapshot.Games);
        Assert.Equal("heroic:gog:1453375253", game.Id);
        Assert.Equal("Stardew Valley", game.Title);
        Assert.Equal("process", game.Launch.Type);
        Assert.Equal("D:/Games/Heroic/Stardew Valley/Stardew Valley.exe", game.Launch.Command);
        Assert.Equal("Stardew Valley.exe", game.ProcessHints.ExecutableName);
    }

    [Fact]
    public async Task ReadsSideloadAppsWithoutAuthFiles()
    {
        using HeroicFixture fixture = HeroicFixture.Create();
        fixture.WriteSideloadApp(
            "sosc.json",
            """
            {
              "appName": "sosc",
              "title": "Saviors of Sapphire Wings / Stranger of Sword City Revisited",
              "installPath": "D:/Games/Saviors",
              "executable": "SoSC.exe"
            }
            """);

        IGameLibraryProvider provider = new HeroicGameLibraryProvider(fixture.Root);
        GameLibrarySnapshot snapshot = await provider.ScanAsync(CancellationToken.None);

        GameDescriptor game = Assert.Single(snapshot.Games);
        Assert.Equal("heroic:sideload:sosc", game.Id);
        Assert.Equal("process", game.Launch.Type);
        Assert.Equal("D:/Games/Saviors/SoSC.exe", game.Launch.Command);
    }

    private sealed class HeroicFixture : IDisposable
    {
        private HeroicFixture(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static HeroicFixture Create()
        {
            string root = Path.Combine(Path.GetTempPath(), $"beacon-heroic-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, "gog_store"));
            Directory.CreateDirectory(Path.Combine(root, "GamesConfig"));
            Directory.CreateDirectory(Path.Combine(root, "sideload_apps"));
            return new HeroicFixture(root);
        }

        public void WriteGogInstalled(string json) =>
            File.WriteAllText(Path.Combine(Root, "gog_store", "installed.json"), json);

        public void WriteGameConfig(string name, string json) =>
            File.WriteAllText(Path.Combine(Root, "GamesConfig", name), json);

        public void WriteSideloadApp(string name, string json) =>
            File.WriteAllText(Path.Combine(Root, "sideload_apps", name), json);

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
