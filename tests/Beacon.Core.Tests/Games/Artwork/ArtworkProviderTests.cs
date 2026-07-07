using Beacon.Core.Games;
using Beacon.Core.Games.Artwork;

namespace Beacon.Core.Tests.Games.Artwork;

public sealed class ArtworkProviderTests
{
    [Fact]
    public async Task GeneratedFallbackCoverWritesReadableSvg()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new GeneratedFallbackCoverProvider(root);

            GameArtwork artwork = await provider.GetArtworkAsync(
                new GameArtworkRequest("Dispatch", "manual:dispatch", null),
                CancellationToken.None);

            Assert.Equal("generated", artwork.Source);
            Assert.NotNull(artwork.CoverPath);
            string svg = await File.ReadAllTextAsync(artwork.CoverPath);
            Assert.Contains("Dispatch", svg, StringComparison.Ordinal);
            Assert.Contains("<svg", svg, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GeneratedFallbackCoverUsesStableAccent()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new GeneratedFallbackCoverProvider(root);

            GameArtwork artwork = await provider.GetArtworkAsync(
                new GameArtworkRequest("Dispatch", "manual:dispatch", null),
                CancellationToken.None);

            Assert.NotNull(artwork.CoverPath);
            string svg = await File.ReadAllTextAsync(artwork.CoverPath);
            Assert.Contains("hsl(265, 78%, 58%)", svg, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
