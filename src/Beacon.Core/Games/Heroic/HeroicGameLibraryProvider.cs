using System.Text.Json;

namespace Beacon.Core.Games.Heroic;

public sealed class HeroicGameLibraryProvider(string heroicRoot) : IGameLibraryProvider
{
    public string Name => "heroic";

    public Task<GameLibrarySnapshot> ScanAsync(CancellationToken cancellationToken)
    {
        var games = new List<GameDescriptor>();
        var diagnostics = new List<string>();

        if (!Directory.Exists(heroicRoot))
        {
            diagnostics.Add($"Heroic root '{heroicRoot}' does not exist.");
            return Task.FromResult(new GameLibrarySnapshot(games, diagnostics));
        }

        ReadGogInstalled(games, diagnostics);
        ReadSideloadApps(games, diagnostics, cancellationToken);

        return Task.FromResult(new GameLibrarySnapshot(games, diagnostics));
    }

    private void ReadGogInstalled(List<GameDescriptor> games, List<string> diagnostics)
    {
        string installedPath = Path.Combine(heroicRoot, "gog_store", "installed.json");
        if (!File.Exists(installedPath))
        {
            diagnostics.Add($"Heroic GOG installed file '{installedPath}' was not found.");
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(installedPath));
            if (!document.RootElement.TryGetProperty("installed", out JsonElement installed) || installed.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add($"Heroic GOG installed file '{installedPath}' does not contain an installed array.");
                return;
            }

            foreach (JsonElement entry in installed.EnumerateArray())
            {
                string? appName = ReadString(entry, "appName", "app_name", "id");
                if (string.IsNullOrWhiteSpace(appName))
                {
                    diagnostics.Add("Skipped Heroic GOG entry without appName.");
                    continue;
                }

                string title = ReadString(entry, "title", "name") ?? ReadGameConfigTitle(appName) ?? appName;
                string installPath = ReadString(entry, "install_path", "installPath", "installPathOverride") ?? string.Empty;
                string executable = ReadString(entry, "executable", "Executable") ?? string.Empty;
                string command = BuildCommand(installPath, executable);

                games.Add(new GameDescriptor(
                    Id: $"heroic:gog:{appName}",
                    Title: title,
                    Source: "heroic-gog",
                    Launch: new GameLaunchIntent("process", command),
                    Artwork: new GameArtwork(null, "none"),
                    Installed: !string.IsNullOrWhiteSpace(command),
                    ProcessHints: new GameProcessHints(Path.GetFileName(command), installPath)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add($"Could not read Heroic GOG installed file '{installedPath}': {ex.Message}");
        }
    }

    private void ReadSideloadApps(List<GameDescriptor> games, List<string> diagnostics, CancellationToken cancellationToken)
    {
        string sideloadPath = Path.Combine(heroicRoot, "sideload_apps");
        if (!Directory.Exists(sideloadPath))
        {
            diagnostics.Add($"Heroic sideload folder '{sideloadPath}' was not found.");
            return;
        }

        foreach (string file in Directory.EnumerateFiles(sideloadPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                JsonElement entry = document.RootElement;
                string appName = ReadString(entry, "appName", "app_name", "id") ?? Path.GetFileNameWithoutExtension(file);
                string title = ReadString(entry, "title", "name") ?? appName;
                string installPath = ReadString(entry, "installPath", "install_path", "path") ?? string.Empty;
                string executable = ReadString(entry, "executable", "Executable", "exe") ?? string.Empty;
                string command = BuildCommand(installPath, executable);

                games.Add(new GameDescriptor(
                    Id: $"heroic:sideload:{appName}",
                    Title: title,
                    Source: "heroic-sideload",
                    Launch: new GameLaunchIntent("process", command),
                    Artwork: new GameArtwork(null, "none"),
                    Installed: !string.IsNullOrWhiteSpace(command),
                    ProcessHints: new GameProcessHints(Path.GetFileName(command), installPath)));
            }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add($"Could not read Heroic sideload app '{file}': {ex.Message}");
        }
        }
    }

    private string? ReadGameConfigTitle(string appName)
    {
        string configPath = Path.Combine(heroicRoot, "GamesConfig", $"{appName}.json");
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            return ReadString(document.RootElement, "title", "name");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
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

    private static string BuildCommand(string installPath, string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return installPath;
        }

        if (Path.IsPathRooted(executable) || string.IsNullOrWhiteSpace(installPath))
        {
            return executable;
        }

        char separator = installPath.Contains('/', StringComparison.Ordinal) && !installPath.Contains('\\', StringComparison.Ordinal)
            ? '/'
            : Path.DirectorySeparatorChar;

        return installPath.TrimEnd('/', '\\') + separator + executable.TrimStart('/', '\\');
    }
}
