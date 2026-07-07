using System.Text.Json;

namespace Beacon.Core.Games.Manual;

public sealed class ManualGameLibraryProvider(string filePath) : IGameLibraryProvider
{
    public string Name => "manual";

    public Task<GameLibrarySnapshot> ScanAsync(CancellationToken cancellationToken)
    {
        var games = new List<GameDescriptor>();
        var diagnostics = new List<string>();

        if (!File.Exists(filePath))
        {
            diagnostics.Add($"Manual game file '{filePath}' does not exist.");
            return Task.FromResult(new GameLibrarySnapshot(games, diagnostics));
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath));
            if (!document.RootElement.TryGetProperty("games", out JsonElement gameArray) || gameArray.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add($"Manual game file '{filePath}' does not contain a games array.");
                return Task.FromResult(new GameLibrarySnapshot(games, diagnostics));
            }

            foreach (JsonElement entry in gameArray.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                GameDescriptor? game = ReadGame(entry, diagnostics);
                if (game is not null)
                {
                    games.Add(game);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add($"Could not read manual game file '{filePath}': {ex.Message}");
        }

        return Task.FromResult(new GameLibrarySnapshot(games, diagnostics));
    }

    private static GameDescriptor? ReadGame(JsonElement entry, List<string> diagnostics)
    {
        string? id = ReadString(entry, "id", "appId");
        string? title = ReadString(entry, "title", "name");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            diagnostics.Add("Skipped manual game entry without id or title.");
            return null;
        }

        string source = ReadString(entry, "source") ?? "manual";
        JsonElement launchElement = ReadObject(entry, "launch");
        string launchType = ReadString(launchElement, "type") ?? "process";
        string launchCommand = ReadString(launchElement, "command") ?? ReadString(entry, "command") ?? id;

        JsonElement artworkElement = ReadObject(entry, "artwork");
        var artwork = new GameArtwork(
            ReadString(artworkElement, "cover", "coverPath"),
            ReadString(artworkElement, "source") ?? "none");

        JsonElement hintsElement = ReadObject(entry, "processHints");
        var hints = new GameProcessHints(
            ReadString(hintsElement, "executableName") ?? Path.GetFileName(launchCommand),
            ReadString(hintsElement, "workingDirectory") ?? Path.GetDirectoryName(launchCommand));

        return new GameDescriptor(
            id,
            title,
            source,
            new GameLaunchIntent(launchType, launchCommand),
            artwork,
            ReadBool(entry, "installed") ?? true,
            hints);
    }

    private static JsonElement ReadObject(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        return default;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return null;
    }
}
