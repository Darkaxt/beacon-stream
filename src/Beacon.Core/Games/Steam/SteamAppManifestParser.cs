namespace Beacon.Core.Games.Steam;

public sealed record SteamAppManifest(int AppId, string Name, string InstallPath, bool Installed);

public static class SteamAppManifestParser
{
    public static SteamAppManifest Parse(string text, string manifestPath)
    {
        ValveKeyValueObject root = ValveKeyValueParser.Parse(text);
        if (!root.TryGetObject("AppState", out ValveKeyValueObject appState))
        {
            throw new FormatException("Steam app manifest is missing AppState.");
        }

        if (!appState.TryGetString("appid", out string appIdValue) || !int.TryParse(appIdValue, out int appId))
        {
            throw new FormatException("Steam app manifest is missing a valid appid.");
        }

        if (!appState.TryGetString("name", out string name))
        {
            name = $"Steam App {appId}";
        }

        if (!appState.TryGetString("installdir", out string installDir))
        {
            installDir = appId.ToString();
        }

        int stateFlags = appState.TryGetString("StateFlags", out string stateFlagsValue) && int.TryParse(stateFlagsValue, out int parsed)
            ? parsed
            : 0;

        string steamAppsDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
        string installPath = Path.Combine(steamAppsDirectory, "common", installDir);

        return new SteamAppManifest(appId, name, installPath, (stateFlags & 4) == 4);
    }
}
