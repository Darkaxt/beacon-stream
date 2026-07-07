namespace Beacon.Core.Games.Artwork;

public sealed class NoArtworkProvider : IArtworkProvider
{
    public Task<GameArtwork> GetArtworkAsync(GameArtworkRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new GameArtwork(null, "none"));
}
