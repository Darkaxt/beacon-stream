using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beacon.ProductionAcceptance;

internal sealed record ProductionDisplayGuardOptions(
    int AcceptanceProcessId,
    string ClientId,
    string RepositoryRoot,
    string ReadyEventName,
    string CompletionEventName,
    string LogPath)
{
    private static readonly Regex ClientIdPattern = new(
        "^gate5-(emulator|physical)-[0-9a-f]{32}\\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static ProductionDisplayGuardOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.Count % 2 != 0)
        {
            throw Invalid();
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Count; index += 2)
        {
            string option = args[index];
            if (option is not (
                    "--acceptance-process-id" or
                    "--client-id" or
                    "--repository-root" or
                    "--ready-event" or
                    "--completion-event" or
                    "--log-path") ||
                !values.TryAdd(option, args[index + 1]))
            {
                throw Invalid();
            }
        }

        if (!values.TryGetValue("--acceptance-process-id", out string? processValue) ||
            !int.TryParse(processValue, out int processId) ||
            processId <= 0 ||
            !values.TryGetValue("--client-id", out string? clientId) ||
            !ClientIdPattern.IsMatch(clientId) ||
            !values.TryGetValue("--repository-root", out string? repositoryRoot) ||
            string.IsNullOrWhiteSpace(repositoryRoot) ||
            !values.TryGetValue("--ready-event", out string? readyEvent) ||
            !IsSafeEventName(readyEvent) ||
            !values.TryGetValue("--completion-event", out string? completionEvent) ||
            !IsSafeEventName(completionEvent) ||
            !values.TryGetValue("--log-path", out string? logPath) ||
            string.IsNullOrWhiteSpace(logPath))
        {
            throw Invalid();
        }

        return new ProductionDisplayGuardOptions(
            processId,
            clientId,
            Path.GetFullPath(repositoryRoot),
            readyEvent,
            completionEvent,
            Path.GetFullPath(logPath));
    }

    private static bool IsSafeEventName(string value) =>
        value.StartsWith("Local\\Beacon.Gate5.", StringComparison.Ordinal) &&
        value.Length <= 160 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '\\' or '.' or '-');

    private static ArgumentException Invalid() => new(
        "Usage: display-guard --acceptance-process-id <pid> --client-id <id> " +
        "--repository-root <path> --ready-event <name> --completion-event <name> --log-path <path>.");
}

internal sealed record ProductionDisplayGuardCommandResult(
    string Command,
    int ExitCode,
    string Output,
    string Error);

internal static class ProductionDisplayRecoveryVerifier
{
    private static readonly string[] RequiredCommands =
    [
        "restore-before-remove",
        "remove",
        "restore-after-remove",
        "display-status",
        "host-agent-status",
    ];

    public static ProductionDisplayRecoveryResult Verify(
        IReadOnlyList<ProductionDisplayGuardCommandResult> commands,
        string expectedClientId)
    {
        Dictionary<string, ProductionDisplayGuardCommandResult> byName;
        try
        {
            byName = commands.ToDictionary(command => command.Command, StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            return ProductionDisplayRecoveryResult.Failed("Display recovery emitted duplicate command evidence.");
        }

        foreach (string name in RequiredCommands)
        {
            if (!byName.TryGetValue(name, out ProductionDisplayGuardCommandResult? command))
            {
                return ProductionDisplayRecoveryResult.Failed(
                    $"Display recovery did not execute required command '{name}'.");
            }
            if (command.ExitCode != 0 &&
                !IsAlreadyAbsentRemove(name, command, expectedClientId))
            {
                return ProductionDisplayRecoveryResult.Failed(
                    $"Display recovery command '{name}' failed with exit code {command.ExitCode}: " +
                    FirstDiagnostic(command));
            }
        }

        string displayStatus = byName["display-status"].Output;
        if (!displayStatus.Contains("mirrorMode=False", StringComparison.Ordinal) ||
            !displayStatus.Contains("physicalPrimaryVerified=True", StringComparison.Ordinal) ||
            !displayStatus.Contains("kind=Physical", StringComparison.Ordinal) ||
            !displayStatus.Contains("primary=True", StringComparison.Ordinal) ||
            displayStatus.Contains("kind=Virtual", StringComparison.Ordinal))
        {
            return ProductionDisplayRecoveryResult.Failed(
                "Display recovery verification did not prove physical-only primary topology with mirror mode disabled.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(byName["host-agent-status"].Output);
            JsonElement root = document.RootElement;
            JsonElement lease = root.GetProperty("payload").GetProperty("lease");
            if (!root.GetProperty("success").GetBoolean() ||
                lease.GetProperty("leaseCount").GetInt32() != 0 ||
                lease.GetProperty("heartbeatActive").GetBoolean())
            {
                return ProductionDisplayRecoveryResult.Failed(
                    "Host Agent recovery verification did not prove zero display leases and an inactive heartbeat.");
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return ProductionDisplayRecoveryResult.Failed(
                $"Host Agent recovery verification was invalid: {error.Message}");
        }

        return ProductionDisplayRecoveryResult.Verified(
            "Physical-only primary topology verified; mirror mode disabled; Host Agent lease count is zero.");
    }

    private static string FirstDiagnostic(ProductionDisplayGuardCommandResult command) =>
        string.IsNullOrWhiteSpace(command.Error)
            ? command.Output.Trim()
            : command.Error.Trim();

    private static bool IsAlreadyAbsentRemove(
        string name,
        ProductionDisplayGuardCommandResult command,
        string expectedClientId) =>
        string.Equals(name, "remove", StringComparison.Ordinal) &&
        command.ExitCode == 2 &&
        string.IsNullOrWhiteSpace(command.Error) &&
        string.Equals(
            command.Output.Trim(),
            $"remove: failed: No active SudoVDA driver lease owns client-{expectedClientId}.",
            StringComparison.Ordinal);
}
