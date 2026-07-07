namespace Beacon.Core.Games.Artwork;

public sealed class GeneratedFallbackCoverProvider(string rootDirectory) : IArtworkProvider
{
    public async Task<GameArtwork> GetArtworkAsync(GameArtworkRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(rootDirectory);
        string path = Path.Combine(rootDirectory, $"{SanitizeFileName(request.GameId)}.svg");
        string svg = CreateSvg(request.Title, request.GameId);
        await File.WriteAllTextAsync(path, svg, cancellationToken);
        return new GameArtwork(path, "generated");
    }

    private static string CreateSvg(string title, string gameId)
    {
        string accent = CreateAccent(gameId);
        string escapedTitle = EscapeXml(title);
        return $$"""
        <svg xmlns="http://www.w3.org/2000/svg" width="600" height="900" viewBox="0 0 600 900">
          <rect width="600" height="900" fill="#14161f"/>
          <rect x="36" y="36" width="528" height="828" fill="none" stroke="{{accent}}" stroke-width="8"/>
          <text x="300" y="430" fill="#f5f7fb" font-family="Segoe UI, Arial, sans-serif" font-size="54" font-weight="700" text-anchor="middle">{{escapedTitle}}</text>
          <text x="300" y="810" fill="{{accent}}" font-family="Segoe UI, Arial, sans-serif" font-size="24" text-anchor="middle">Beacon Stream</text>
        </svg>
        """;
    }

    private static string CreateAccent(string value)
    {
        int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(value);
        int hue = Math.Abs(hash % 360);
        return $"hsl({hue}, 78%, 58%)";
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) || character is ':' or '/' or '\\' ? '_' : character));
    }

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
