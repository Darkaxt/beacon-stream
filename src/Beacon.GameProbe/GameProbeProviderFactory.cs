using Beacon.Core.Games;
using Beacon.Core.Games.Heroic;
using Beacon.Core.Games.Hydra;
using Beacon.Core.Games.Manual;
using Beacon.Core.Games.Steam;

namespace Beacon.GameProbe;

public static class GameProbeProviderFactory
{
    public static IReadOnlyList<IGameLibraryProvider> CreateProviders(ScanGameProbeCommand command)
    {
        var providers = new List<IGameLibraryProvider>();

        foreach (string steamRoot in ResolveSteamRoots(command.SteamRoot))
        {
            providers.Add(new SteamGameLibraryProvider(steamRoot));
        }

        string? heroicRoot = ResolveHeroicRoot(command.HeroicRoot);
        if (!string.IsNullOrWhiteSpace(heroicRoot))
        {
            providers.Add(new HeroicGameLibraryProvider(heroicRoot));
        }

        string? hydraDatabase = ResolveHydraDatabase(command.HydraDatabasePath);
        if (!string.IsNullOrWhiteSpace(hydraDatabase))
        {
            providers.Add(new HydraGameLibraryProvider(hydraDatabase));
        }

        if (!string.IsNullOrWhiteSpace(command.ManualGamesPath))
        {
            providers.Add(new ManualGameLibraryProvider(command.ManualGamesPath));
        }

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

        return existing.Length > 0 ? existing : [Path.Combine(GetProgramFilesX86(), "Steam")];
    }

    private static string? ResolveHeroicRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return explicitRoot;
        }

        string configured = Environment.GetEnvironmentVariable("BEACON_HEROIC_ROOT") ?? string.Empty;
        return !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(GetApplicationData(), "heroic");
    }

    private static string? ResolveHydraDatabase(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        string configured = Environment.GetEnvironmentVariable("BEACON_HYDRA_DB") ?? string.Empty;
        return !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(GetApplicationData(), "hydra", "hydra.db");
    }

    private static void AddIfConfigured(List<string> candidates, string variableName)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            candidates.Add(value);
        }
    }

    private static void AddProgramFilesCandidate(List<string> candidates, Environment.SpecialFolder folder)
    {
        string root = Environment.GetFolderPath(folder);
        if (!string.IsNullOrWhiteSpace(root))
        {
            candidates.Add(Path.Combine(root, "Steam"));
        }
    }

    private static string GetApplicationData() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static string GetProgramFilesX86()
    {
        string path = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return string.IsNullOrWhiteSpace(path) ? "C:/Program Files (x86)" : path;
    }
}
