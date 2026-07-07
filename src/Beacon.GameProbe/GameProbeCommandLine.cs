namespace Beacon.GameProbe;

public abstract record GameProbeCommand;

public sealed record ScanGameProbeCommand(
    bool Json,
    string? SteamRoot,
    string? HeroicRoot,
    string? HydraDatabasePath,
    string? ManualGamesPath) : GameProbeCommand;

public sealed record SteamShortcutsGameProbeCommand(string Path) : GameProbeCommand;

public static class GameProbeCommandLine
{
    public static GameProbeCommand Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return ParseScan(args);
        }

        return args[0].ToLowerInvariant() switch
        {
            "scan" => ParseScan(args),
            "steam-shortcuts" => ParseSteamShortcuts(args),
            _ => throw new ArgumentException($"Unknown game probe command '{args[0]}'.", nameof(args))
        };
    }

    private static ScanGameProbeCommand ParseScan(IReadOnlyList<string> args) =>
        new(
            Json: HasFlag(args, "--json"),
            SteamRoot: ReadOptionalOption(args, "--steam-root"),
            HeroicRoot: ReadOptionalOption(args, "--heroic-root"),
            HydraDatabasePath: ReadOptionalOption(args, "--hydra-db"),
            ManualGamesPath: ReadOptionalOption(args, "--manual-games"));

    private static SteamShortcutsGameProbeCommand ParseSteamShortcuts(IReadOnlyList<string> args)
    {
        if (args.Count != 2)
        {
            throw new ArgumentException("Command steam-shortcuts requires one shortcuts.vdf path.", nameof(args));
        }

        return new SteamShortcutsGameProbeCommand(args[1]);
    }

    private static bool HasFlag(IReadOnlyList<string> args, string flag) =>
        args.Any(arg => arg.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string? ReadOptionalOption(IReadOnlyList<string> args, string optionName)
    {
        for (int index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
