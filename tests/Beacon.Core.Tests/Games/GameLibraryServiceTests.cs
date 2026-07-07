using Beacon.Core.Games;
using Beacon.Core.Games.Artwork;

namespace Beacon.Core.Tests.Games;

public sealed class GameLibraryServiceTests
{
    [Fact]
    public async Task DedupesLegacySteamMappingsWhenAutomaticDiscoveryCoversThem()
    {
        var steam = new FakeProvider("steam", new GameDescriptor(
            "steam:1086940",
            "Baldur's Gate 3",
            "steam",
            new GameLaunchIntent("steam-app", "steam://run/1086940"),
            new GameArtwork(null, "none"),
            true,
            new GameProcessHints(null, null)));

        var manual = new FakeProvider("manual", new GameDescriptor(
            "legacy-sunshine:BG3",
            "Baldur's Gate 3",
            "legacy-sunshine",
            new GameLaunchIntent("steam-app", "steam://run/1086940"),
            new GameArtwork(null, "none"),
            true,
            new GameProcessHints(null, null)));

        var service = new GameLibraryService([manual, steam], new PassthroughArtworkProvider());

        GameLibrarySnapshot snapshot = await service.ScanAsync(CancellationToken.None);

        GameDescriptor game = Assert.Single(snapshot.Games);
        Assert.Equal("steam:1086940", game.Id);
    }

    [Fact]
    public async Task EnrichesMissingArtwork()
    {
        var provider = new FakeProvider("manual", new GameDescriptor(
            "manual:dispatch",
            "Dispatch",
            "manual",
            new GameLaunchIntent("process", "D:/Games/Dispatch.exe"),
            new GameArtwork(null, "none"),
            true,
            new GameProcessHints("Dispatch.exe", "D:/Games")));

        var service = new GameLibraryService([provider], new StaticArtworkProvider(new GameArtwork("C:/art/dispatch.png", "generated")));

        GameLibrarySnapshot snapshot = await service.ScanAsync(CancellationToken.None);

        GameDescriptor game = Assert.Single(snapshot.Games);
        Assert.Equal("generated", game.Artwork.Source);
        Assert.Equal("C:/art/dispatch.png", game.Artwork.CoverPath);
    }

    private sealed class FakeProvider(string name, params GameDescriptor[] games) : IGameLibraryProvider
    {
        public string Name => name;

        public Task<GameLibrarySnapshot> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new GameLibrarySnapshot(games, []));
    }

    private sealed class PassthroughArtworkProvider : IArtworkProvider
    {
        public Task<GameArtwork> GetArtworkAsync(GameArtworkRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new GameArtwork(null, "none"));
    }

    private sealed class StaticArtworkProvider(GameArtwork artwork) : IArtworkProvider
    {
        public Task<GameArtwork> GetArtworkAsync(GameArtworkRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(artwork);
    }
}
