using Beacon.Core.Games.Artwork;

namespace Beacon.Core.Games;

public sealed class GameLibraryService(IReadOnlyList<IGameLibraryProvider> providers, IArtworkProvider artworkProvider)
{
    public async Task<GameLibrarySnapshot> ScanAsync(CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var gamesByLaunch = new Dictionary<string, PrioritizedGame>(StringComparer.OrdinalIgnoreCase);

        foreach (IGameLibraryProvider provider in providers)
        {
            GameLibrarySnapshot snapshot = await provider.ScanAsync(cancellationToken);
            diagnostics.AddRange(snapshot.Diagnostics);

            foreach (GameDescriptor game in snapshot.Games)
            {
                string key = CreateDedupeKey(game);
                int priority = GetSourcePriority(game.Source);
                if (!gamesByLaunch.TryGetValue(key, out PrioritizedGame? existing) || priority > existing.Priority)
                {
                    gamesByLaunch[key] = new PrioritizedGame(priority, game);
                }
            }
        }

        var enriched = new List<GameDescriptor>();
        foreach (PrioritizedGame prioritized in gamesByLaunch.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            enriched.Add(await EnrichArtworkAsync(prioritized.Game, cancellationToken));
        }

        return new GameLibrarySnapshot(enriched.OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase).ToArray(), diagnostics);
    }

    private async Task<GameDescriptor> EnrichArtworkAsync(GameDescriptor game, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(game.Artwork.CoverPath) && !game.Artwork.Source.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return game;
        }

        GameArtwork artwork = await artworkProvider.GetArtworkAsync(
            new GameArtworkRequest(game.Title, game.Id, GetSteamAppId(game)),
            cancellationToken);

        return !string.IsNullOrWhiteSpace(artwork.CoverPath) && !artwork.Source.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? game with { Artwork = artwork }
            : game;
    }

    private static string CreateDedupeKey(GameDescriptor game) =>
        string.IsNullOrWhiteSpace(game.Launch.Command)
            ? $"id:{game.Id}"
            : $"launch:{game.Launch.Type}:{game.Launch.Command}".Trim().ToUpperInvariant();

    private static int GetSourcePriority(string source)
    {
        if (source.Equals("steam", StringComparison.OrdinalIgnoreCase) ||
            source.Equals("steam-shortcut", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (source.StartsWith("heroic", StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (source.StartsWith("hydra", StringComparison.OrdinalIgnoreCase))
        {
            return 70;
        }

        if (source.Equals("manual", StringComparison.OrdinalIgnoreCase))
        {
            return 50;
        }

        return source.StartsWith("legacy", StringComparison.OrdinalIgnoreCase) ? 10 : 40;
    }

    private static int? GetSteamAppId(GameDescriptor game)
    {
        if (game.Source.Equals("steam", StringComparison.OrdinalIgnoreCase) &&
            game.Id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(game.Id["steam:".Length..], out int appId))
        {
            return appId;
        }

        return null;
    }

    private sealed record PrioritizedGame(int Priority, GameDescriptor Game);
}
