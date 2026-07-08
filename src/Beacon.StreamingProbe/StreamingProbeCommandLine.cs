namespace Beacon.StreamingProbe;

public sealed record StreamingProbeCommand(
    string SessionId,
    string DisplayId,
    string DescriptorPath,
    string Protocol,
    string LaunchUri,
    IReadOnlyDictionary<string, string> Endpoints,
    bool Once,
    string? ChildExecutable = null,
    string? ChildArguments = null,
    IReadOnlyDictionary<string, string>? ChildEnvironment = null);

public static class StreamingProbeCommandLine
{
    public static StreamingProbeCommand Parse(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        environment ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int startIndex = args.Count > 0 && string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

        string sessionId = ReadRequiredValue(args, startIndex, environment, "--session", "BEACON_SESSION_ID");
        string displayId = ReadRequiredValue(args, startIndex, environment, "--display", "BEACON_DISPLAY_ID");
        string descriptorPath = ReadRequiredValue(
            args,
            startIndex,
            environment,
            "--stream-session-descriptor",
            "BEACON_STREAM_SESSION_DESCRIPTOR_PATH");
        string protocol = ReadOptionalValue(args, startIndex, environment, "--protocol", "BEACON_CONNECTION_PROTOCOL")
            ?? "gamestream";
        string launchUri = ReadOptionalValue(args, startIndex, environment, "--launch-uri", "BEACON_CONNECTION_LAUNCH_URI")
            ?? $"moonlight://beacon/probe/{Uri.EscapeDataString(sessionId)}";
        IReadOnlyDictionary<string, string> endpoints = ParseEndpoints(
            ReadOptionalValue(args, startIndex, environment, "--endpoints", "BEACON_CONNECTION_ENDPOINTS"));
        string? childExecutable = ReadOptionalValue(args, startIndex, environment, "--child-executable", "BEACON_WRAPPER_CHILD_EXECUTABLE");
        string? childArguments = ReadOptionalValue(args, startIndex, environment, "--child-arguments", "BEACON_WRAPPER_CHILD_ARGUMENTS");

        return new StreamingProbeCommand(
            sessionId,
            displayId,
            descriptorPath,
            protocol,
            launchUri,
            endpoints,
            HasFlag(args, startIndex, "--once"),
            childExecutable,
            childArguments,
            CreateChildEnvironment(environment, sessionId, displayId, descriptorPath, protocol, launchUri, endpoints, childExecutable, childArguments));
    }

    private static string ReadRequiredValue(
        IReadOnlyList<string> args,
        int startIndex,
        IReadOnlyDictionary<string, string> environment,
        string optionName,
        string environmentName) =>
        ReadOptionalValue(args, startIndex, environment, optionName, environmentName) ??
        throw new ArgumentException($"Missing required option {optionName} or environment variable {environmentName}.", nameof(args));

    private static string? ReadOptionalValue(
        IReadOnlyList<string> args,
        int startIndex,
        IReadOnlyDictionary<string, string> environment,
        string optionName,
        string environmentName)
    {
        for (int index = startIndex; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(args[index + 1]) ? null : args[index + 1];
            }
        }

        return environment.TryGetValue(environmentName, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static bool HasFlag(IReadOnlyList<string> args, int startIndex, string optionName)
    {
        for (int index = startIndex; index < args.Count; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> ParseEndpoints(string? value)
    {
        var endpoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (string item in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] parts = item.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    endpoints[parts[0]] = parts[1];
                }
            }
        }

        if (endpoints.Count == 0)
        {
            endpoints["input"] = "udp://127.0.0.1:48000";
            endpoints["rtsp"] = "rtsp://127.0.0.1:48010/beacon";
        }

        return endpoints;
    }

    private static IReadOnlyDictionary<string, string> CreateChildEnvironment(
        IReadOnlyDictionary<string, string> environment,
        string sessionId,
        string displayId,
        string descriptorPath,
        string protocol,
        string launchUri,
        IReadOnlyDictionary<string, string> endpoints,
        string? childExecutable,
        string? childArguments)
    {
        var childEnvironment = new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase)
        {
            ["BEACON_SESSION_ID"] = sessionId,
            ["BEACON_DISPLAY_ID"] = displayId,
            ["BEACON_STREAM_SESSION_DESCRIPTOR_PATH"] = descriptorPath,
            ["BEACON_CONNECTION_PROTOCOL"] = protocol,
            ["BEACON_CONNECTION_LAUNCH_URI"] = launchUri,
            ["BEACON_CONNECTION_ENDPOINTS"] = FormatEndpoints(endpoints)
        };

        if (!string.IsNullOrWhiteSpace(childExecutable))
        {
            childEnvironment["BEACON_WRAPPER_CHILD_EXECUTABLE"] = childExecutable.Trim();
        }

        if (!string.IsNullOrWhiteSpace(childArguments))
        {
            childEnvironment["BEACON_WRAPPER_CHILD_ARGUMENTS"] = childArguments.Trim();
        }

        return childEnvironment;
    }

    private static string FormatEndpoints(IReadOnlyDictionary<string, string> endpoints) =>
        string.Join(
            ';',
            endpoints
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key.Trim()}={pair.Value.Trim()}"));
}
