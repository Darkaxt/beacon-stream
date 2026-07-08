namespace Beacon.DisplayProbe;

public abstract record DisplayProbeCommand;

public sealed record StatusDisplayProbeCommand : DisplayProbeCommand;

public sealed record PrepareDisplayProbeCommand(
    string ClientId,
    int Width,
    int Height,
    int RefreshHz,
    string Hdr) : DisplayProbeCommand;

public sealed record EnsureDisplayProbeCommand(
    string ClientId,
    int Width,
    int Height,
    int RefreshHz,
    string Hdr) : DisplayProbeCommand;

public sealed record PrimaryDisplayProbeCommand(string ClientId) : DisplayProbeCommand;

public sealed record RestorePhysicalDisplayProbeCommand : DisplayProbeCommand;

public sealed record RemoveDisplayProbeCommand(string ClientId) : DisplayProbeCommand;

public static class DisplayProbeCommandLine
{
    public static DisplayProbeCommand Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new StatusDisplayProbeCommand();
        }

        return args[0].ToLowerInvariant() switch
        {
            "status" => new StatusDisplayProbeCommand(),
            "prepare" => ParsePrepare(args),
            "ensure" => ParseEnsure(args),
            "primary" => new PrimaryDisplayProbeCommand(ReadRequiredOption(args, "--client")),
            "restore-physical" => new RestorePhysicalDisplayProbeCommand(),
            "remove" => new RemoveDisplayProbeCommand(ReadRequiredOption(args, "--client")),
            _ => throw new ArgumentException($"Unknown display probe command '{args[0]}'.", nameof(args))
        };
    }

    private static PrepareDisplayProbeCommand ParsePrepare(IReadOnlyList<string> args)
    {
        DisplayProbeModeOptions options = ParseModeOptions(args);
        return new PrepareDisplayProbeCommand(
            options.ClientId,
            options.Width,
            options.Height,
            options.RefreshHz,
            options.Hdr);
    }

    private static EnsureDisplayProbeCommand ParseEnsure(IReadOnlyList<string> args)
    {
        DisplayProbeModeOptions options = ParseModeOptions(args);

        return new EnsureDisplayProbeCommand(
            options.ClientId,
            options.Width,
            options.Height,
            options.RefreshHz,
            options.Hdr);
    }

    private static DisplayProbeModeOptions ParseModeOptions(IReadOnlyList<string> args)
    {
        string clientId = ReadRequiredOption(args, "--client");
        int width = ReadRequiredInt(args, "--width");
        int height = ReadRequiredInt(args, "--height");
        int refreshHz = ReadRequiredInt(args, "--refresh");
        string hdr = ReadOptionalOption(args, "--hdr") ?? "prefer";

        return new DisplayProbeModeOptions(clientId, width, height, refreshHz, hdr);
    }

    private static int ReadRequiredInt(IReadOnlyList<string> args, string optionName)
    {
        string value = ReadRequiredOption(args, optionName);
        return int.TryParse(value, out int parsed)
            ? parsed
            : throw new ArgumentException($"Option {optionName} must be an integer.", nameof(args));
    }

    private static string ReadRequiredOption(IReadOnlyList<string> args, string optionName) =>
        ReadOptionalOption(args, optionName) ??
        throw new ArgumentException($"Missing required option {optionName}.", nameof(args));

    private static string? ReadOptionalOption(IReadOnlyList<string> args, string optionName)
    {
        for (int index = 1; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private sealed record DisplayProbeModeOptions(
        string ClientId,
        int Width,
        int Height,
        int RefreshHz,
        string Hdr);
}
