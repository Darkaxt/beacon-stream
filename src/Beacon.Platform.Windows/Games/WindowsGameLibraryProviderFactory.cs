using Beacon.Core.Games;
using Beacon.Core.Games.Heroic;
using Beacon.Core.Games.Hydra;
using Beacon.Core.Games.Manual;
using Beacon.Core.Games.Steam;

namespace Beacon.Platform.Windows.Games;

public sealed record WindowsGameLibraryProviderOptions(
    string? SteamRoot,
    string? HeroicRoot,
    string? HydraDatabasePath,
    string? ManualGamesPath);

public static class WindowsGameLibraryProviderFactory
{
    public static IReadOnlyList<IGameLibraryProvider> CreateProviders(
        WindowsGameLibraryProviderOptions options)
    {
        var providers = new List<IGameLibraryProvider>();

        foreach (string steamRoot in ResolveSteamRoots(options.SteamRoot))
        {
            providers.Add(new SteamGameLibraryProvider(steamRoot));
        }

        providers.Add(new HeroicGameLibraryProvider(ResolveHeroicRoot(options.HeroicRoot)));
        providers.Add(new HydraGameLibraryProvider(ResolveHydraDatabase(options.HydraDatabasePath)));
        providers.Add(new ManualGameLibraryProvider(ResolveManualGames(options.ManualGamesPath)));
        return providers;
    }

    private static IReadOnlyList<string> ResolveSteamRoots(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return [explicitRoot];
        }

        var candidates = new List<string>();
        AddIfConfigured(candidates, "BEACON_STEAM_ROOT");
        AddProgramFilesCandidate(candidates, Environment.SpecialFolder.ProgramFilesX86);
        AddProgramFilesCandidate(candidates, Environment.SpecialFolder.ProgramFiles);

        foreach (string drive in Directory.GetLogicalDrives())
        {
            candidates.Add(Path.Combine(drive, "Steam"));
            candidates.Add(Path.Combine(drive, "Games", "Steam"));
        }

        string[] existing = candidates
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return existing.Length > 0
            ? existing
            : [Path.Combine(GetProgramFilesX86(), "Steam")];
    }

    private static string ResolveHeroicRoot(string? explicitRoot) =>
        ResolvePath(
            explicitRoot,
            "BEACON_HEROIC_ROOT",
            Path.Combine(GetApplicationData(), "heroic"));

    private static string ResolveHydraDatabase(string? explicitPath) =>
        ResolvePath(
            explicitPath,
            "BEACON_HYDRA_DB",
            Path.Combine(GetApplicationData(), "hydra", "hydra.db"));

    private static string ResolveManualGames(string? explicitPath) =>
        ResolvePath(
            explicitPath,
            "BEACON_MANUAL_GAMES_PATH",
            Path.Combine(GetLocalApplicationData(), "Beacon Stream", "games.json"));

    private static string ResolvePath(
        string? explicitPath,
        string variableName,
        string fallback)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        string? configured = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }

    private static void AddIfConfigured(List<string> candidates, string variableName)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            candidates.Add(value);
        }
    }

    private static void AddProgramFilesCandidate(
        List<string> candidates,
        Environment.SpecialFolder folder)
    {
        string root = Environment.GetFolderPath(folder);
        if (!string.IsNullOrWhiteSpace(root))
        {
            candidates.Add(Path.Combine(root, "Steam"));
        }
    }

    private static string GetApplicationData() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static string GetLocalApplicationData() =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string GetProgramFilesX86()
    {
        string path = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return string.IsNullOrWhiteSpace(path) ? "C:/Program Files (x86)" : path;
    }
}
