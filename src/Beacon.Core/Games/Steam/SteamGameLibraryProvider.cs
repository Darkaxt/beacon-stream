namespace Beacon.Core.Games.Steam;

public sealed class SteamGameLibraryProvider(string steamRoot) : IGameLibraryProvider
{
    public string Name => "steam";

    public Task<GameLibrarySnapshot> ScanAsync(CancellationToken cancellationToken)
    {
        var games = new List<GameDescriptor>();
        var diagnostics = new List<string>();

        if (!Directory.Exists(steamRoot))
        {
            diagnostics.Add($"Steam root '{steamRoot}' does not exist.");
            return Task.FromResult(new GameLibrarySnapshot(games, diagnostics));
        }

        foreach (SteamLibraryFolder folder in ReadLibraryFolders(diagnostics))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOfficialApps(folder, games, diagnostics);
        }

        ReadShortcuts(games, diagnostics, cancellationToken);
        return Task.FromResult(new GameLibrarySnapshot(games, diagnostics));
    }

    private IReadOnlyList<SteamLibraryFolder> ReadLibraryFolders(List<string> diagnostics)
    {
        string libraryFoldersPath = Path.Combine(steamRoot, "config", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
        {
            diagnostics.Add($"Steam library folder file '{libraryFoldersPath}' was not found.");
            return [];
        }

        try
        {
            return SteamLibraryFoldersParser.Parse(File.ReadAllText(libraryFoldersPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            diagnostics.Add($"Could not read Steam library folders from '{libraryFoldersPath}': {ex.Message}");
            return [];
        }
    }

    private static void ReadOfficialApps(SteamLibraryFolder folder, List<GameDescriptor> games, List<string> diagnostics)
    {
        string steamAppsPath = Path.Combine(folder.Path, "steamapps");
        if (!Directory.Exists(steamAppsPath))
        {
            diagnostics.Add($"Steam library '{steamAppsPath}' does not exist.");
            return;
        }

        foreach (string manifestPath in Directory.EnumerateFiles(steamAppsPath, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
        {
            try
            {
                SteamAppManifest manifest = SteamAppManifestParser.Parse(File.ReadAllText(manifestPath), manifestPath);
                games.Add(new GameDescriptor(
                    Id: $"steam:{manifest.AppId}",
                    Title: manifest.Name,
                    Source: "steam",
                    Launch: new GameLaunchIntent("steam-app", $"steam://run/{manifest.AppId}"),
                    Artwork: new GameArtwork(null, "none"),
                    Installed: manifest.Installed,
                    ProcessHints: new GameProcessHints(null, manifest.InstallPath)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                diagnostics.Add($"Could not read Steam manifest '{manifestPath}': {ex.Message}");
            }
        }
    }

    private void ReadShortcuts(List<GameDescriptor> games, List<string> diagnostics, CancellationToken cancellationToken)
    {
        string userDataPath = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userDataPath))
        {
            diagnostics.Add($"Steam userdata folder '{userDataPath}' was not found.");
            return;
        }

        foreach (string shortcutsPath in Directory.EnumerateFiles(userDataPath, "shortcuts.vdf", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (SteamShortcut shortcut in SteamShortcutBinaryParser.Parse(File.ReadAllBytes(shortcutsPath)))
                {
                    string executablePath = TrimSteamQuotedPath(shortcut.Exe);
                    string workingDirectory = TrimSteamQuotedPath(shortcut.StartDir);
                    games.Add(new GameDescriptor(
                        Id: $"steam-shortcut:{unchecked((uint)shortcut.AppId)}",
                        Title: shortcut.AppName,
                        Source: "steam-shortcut",
                        Launch: new GameLaunchIntent("steam-rungameid", SteamShortcutLaunchId.FromStoredAppId(shortcut.AppId).ToUri()),
                        Artwork: new GameArtwork(null, "none"),
                        Installed: true,
                        ProcessHints: new GameProcessHints(Path.GetFileName(executablePath), workingDirectory)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                diagnostics.Add($"Could not read Steam shortcuts from '{shortcutsPath}': {ex.Message}");
            }
        }
    }

    private static string TrimSteamQuotedPath(string value) =>
        value.Trim().Trim('"');
}
