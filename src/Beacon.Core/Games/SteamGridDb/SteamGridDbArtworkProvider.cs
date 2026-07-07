using System.Net.Http.Headers;
using System.Text.Json;
using Beacon.Core.Games.Artwork;

namespace Beacon.Core.Games.SteamGridDb;

public sealed class SteamGridDbArtworkProvider(HttpClient httpClient, string? apiKey, string rootDirectory) : IArtworkProvider
{
    public async Task<GameArtwork> GetArtworkAsync(GameArtworkRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new GameArtwork(null, "none");
        }

        Directory.CreateDirectory(rootDirectory);

        try
        {
            foreach (int candidateId in await SearchExactCandidateIdsAsync(request.Title, cancellationToken))
            {
                string? gridUrl = await GetFirstUsableGridUrlAsync(candidateId, cancellationToken);
                if (gridUrl is null)
                {
                    continue;
                }

                string path = await DownloadArtworkAsync(request.GameId, gridUrl, cancellationToken);
                return new GameArtwork(path, "steamgriddb");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            return new GameArtwork(null, "none");
        }

        return new GameArtwork(null, "none");
    }

    private async Task<IReadOnlyList<int>> SearchExactCandidateIdsAsync(string title, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest($"/api/v2/search/autocomplete/{Uri.EscapeDataString(title)}");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<int>();
        foreach (JsonElement candidate in data.EnumerateArray())
        {
            if (candidate.TryGetProperty("name", out JsonElement nameElement) &&
                nameElement.ValueKind == JsonValueKind.String &&
                title.Equals(nameElement.GetString(), StringComparison.OrdinalIgnoreCase) &&
                candidate.TryGetProperty("id", out JsonElement idElement) &&
                idElement.TryGetInt32(out int id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private async Task<string?> GetFirstUsableGridUrlAsync(int gameId, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest($"/api/v2/grids/game/{gameId}");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement grid in data.EnumerateArray())
        {
            if (grid.TryGetProperty("url", out JsonElement urlElement) &&
                urlElement.ValueKind == JsonValueKind.String &&
                grid.TryGetProperty("width", out JsonElement widthElement) &&
                widthElement.GetInt32() > 0 &&
                grid.TryGetProperty("height", out JsonElement heightElement) &&
                heightElement.GetInt32() > 0)
            {
                return urlElement.GetString();
            }
        }

        return null;
    }

    private async Task<string> DownloadArtworkAsync(string gameId, string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        string extension = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        string path = Path.Combine(rootDirectory, $"{SanitizeFileName(gameId)}{extension}");
        await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
        return path;
    }

    private HttpRequestMessage CreateRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) || character is ':' or '/' or '\\' ? '_' : character));
    }
}
