namespace Beacon.Core.Games.Artwork;

public sealed record GameArtworkRequest(string Title, string GameId, int? SteamAppId);

public interface IArtworkProvider
{
    Task<GameArtwork> GetArtworkAsync(GameArtworkRequest request, CancellationToken cancellationToken);
}
