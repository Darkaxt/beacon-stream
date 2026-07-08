using System.Globalization;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;

namespace Beacon.Platform.Windows.Streaming;

public sealed record ExternalProcessStreamingOptions(
    string? ExecutablePath,
    string? ConnectionProtocol = null,
    string? ConnectionLaunchUri = null,
    IReadOnlyDictionary<string, string>? ConnectionEndpoints = null,
    string? ManifestPath = null);

public sealed record ExternalStreamingManifest(
    string? Name,
    string? Protocol,
    string? LaunchUri,
    IReadOnlyDictionary<string, string>? Endpoints,
    IReadOnlyList<string>? Codecs,
    int? MaxFps,
    int? MaxBitrateMbps,
    bool Hdr10,
    IReadOnlyList<string>? Transports,
    IReadOnlyList<string>? Encoders,
    IReadOnlyList<string>? Capture,
    IReadOnlyList<string>? Diagnostics);

public sealed record ExternalStreamingManifestReadResult(bool Success, ExternalStreamingManifest? Manifest, string? Error)
{
    public static ExternalStreamingManifestReadResult Ok(ExternalStreamingManifest manifest) => new(true, manifest, null);

    public static ExternalStreamingManifestReadResult Fail(string error) => new(false, null, error);
}

public sealed record ExternalStreamingCommand(
    string FileName,
    string Arguments,
    IReadOnlyDictionary<string, string> Environment);

public sealed record ExternalStreamingProcess(int ProcessId);

public sealed record ExternalStreamingProcessStopResult(bool Success, string? Error)
{
    public static ExternalStreamingProcessStopResult Ok() => new(true, null);

    public static ExternalStreamingProcessStopResult Fail(string error) => new(false, error);
}

public interface IExternalStreamingProcessRunner
{
    bool FileExists(string path);

    ExternalStreamingProcess Start(ExternalStreamingCommand command);

    ExternalStreamingProcessStopResult Stop(ExternalStreamingProcess process);
}

public interface IExternalStreamingManifestReader
{
    bool FileExists(string path);

    ExternalStreamingManifestReadResult Read(string path);
}

public sealed class ExternalProcessStreamingBackend(
    ExternalProcessStreamingOptions options,
    IExternalStreamingProcessRunner runner,
    IExternalStreamingManifestReader? manifestReader = null) : IStreamingBackend
{
    private static readonly ExternalStreamingManifest EmptyManifest = new(
        null,
        null,
        null,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        [],
        null,
        null,
        Hdr10: false,
        [],
        [],
        [],
        []);

    private readonly Lock gate = new();
    private readonly Dictionary<string, StreamingSessionState> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExternalStreamingProcess> processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IExternalStreamingManifestReader manifestReader = manifestReader ?? NoExternalStreamingManifestReader.Instance;

    public Task<StreamingPreflightResult> CheckReadinessAsync(SessionPlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            return Task.FromResult(StreamingPreflightResult.Fail(
                "External streaming executable path is not configured. Set Beacon:Streaming:ExternalProcess:ExecutablePath or BEACON_EXTERNAL_STREAMING_EXECUTABLE."));
        }

        if (!runner.FileExists(options.ExecutablePath))
        {
            return Task.FromResult(StreamingPreflightResult.Fail(
                $"External streaming executable '{options.ExecutablePath}' does not exist."));
        }

        ExternalStreamingManifestReadResult manifest = ReadManifestIfConfigured();
        if (!manifest.Success)
        {
            return Task.FromResult(StreamingPreflightResult.Fail(manifest.Error ?? "External streaming manifest is invalid."));
        }

        if (manifest.Manifest is not null)
        {
            string? compatibilityError = ValidateManifest(plan, manifest.Manifest);
            if (!string.IsNullOrWhiteSpace(compatibilityError))
            {
                return Task.FromResult(StreamingPreflightResult.Fail(compatibilityError));
            }
        }

        return Task.FromResult(StreamingPreflightResult.Ok());
    }

    public async Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken)
    {
        StreamingPreflightResult preflight = await CheckReadinessAsync(plan, cancellationToken);
        if (!preflight.Success)
        {
            return StreamingStartResult.Fail(preflight.Error ?? "External streaming backend is not ready.");
        }

        try
        {
            ExternalStreamingCommand command = CreateStartCommand(options.ExecutablePath!, plan, options);
            ExternalStreamingProcess process = runner.Start(command);
            var session = new StreamingSessionState(
                plan.SessionId,
                plan.ClientId.Value,
                plan.AppId,
                plan.Display.DisplayId,
                plan.Stream.Codec,
                plan.Stream.Fps,
                plan.Stream.InitialBitrateMbps,
                plan.Stream.Transport,
                State: "running",
                Error: null,
                CreateConnectionDescriptor(options));

            lock (gate)
            {
                sessions[plan.SessionId] = session;
                processes[plan.SessionId] = process;
            }

            return StreamingStartResult.Ok(session);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return StreamingStartResult.Fail($"External streaming process failed to start: {ex.Message}");
        }
    }

    public Task<StreamingStopResult> StopAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        StreamingSessionState? session;
        ExternalStreamingProcess? process;
        lock (gate)
        {
            sessions.TryGetValue(sessionId, out session);
            processes.TryGetValue(sessionId, out process);
        }

        if (session is null)
        {
            return Task.FromResult(StreamingStopResult.Fail($"Stream session '{sessionId}' is not running."));
        }

        if (process is not null)
        {
            ExternalStreamingProcessStopResult stop = runner.Stop(process);
            if (!stop.Success)
            {
                StreamingSessionState failed = session with { State = "stop-failed", Error = stop.Error };
                lock (gate)
                {
                    sessions[sessionId] = failed;
                }

                return Task.FromResult(StreamingStopResult.Fail(stop.Error ?? $"External streaming process {process.ProcessId} did not stop."));
            }
        }

        StreamingSessionState stopped = session with { State = "stopped", Error = null };
        lock (gate)
        {
            sessions[sessionId] = stopped;
            processes.Remove(sessionId);
        }

        return Task.FromResult(StreamingStopResult.Ok(stopped));
    }

    public Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            return Task.FromResult(sessions.GetValueOrDefault(sessionId));
        }
    }

    public IReadOnlyList<StreamingSessionState> GetSessions()
    {
        lock (gate)
        {
            return sessions.Values
                .OrderBy(session => session.ClientId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public static ExternalStreamingCommand CreateStartCommand(
        string executablePath,
        SessionPlan plan,
        ExternalProcessStreamingOptions? options = null)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BEACON_SESSION_ID"] = plan.SessionId,
            ["BEACON_CLIENT_ID"] = plan.ClientId.Value,
            ["BEACON_APP_ID"] = plan.AppId,
            ["BEACON_DISPLAY_ID"] = plan.Display.DisplayId,
            ["BEACON_STREAM_CODEC"] = plan.Stream.Codec,
            ["BEACON_STREAM_FPS"] = plan.Stream.Fps.ToString(CultureInfo.InvariantCulture),
            ["BEACON_STREAM_BITRATE_MBPS"] = plan.Stream.InitialBitrateMbps.ToString(CultureInfo.InvariantCulture),
            ["BEACON_STREAM_TRANSPORT"] = plan.Stream.Transport
        };

        AddConnectionEnvironment(environment, options);

        string arguments = $"--session \"{plan.SessionId}\" --display \"{plan.Display.DisplayId}\"";
        return new ExternalStreamingCommand(executablePath, arguments, environment);
    }

    private static void AddConnectionEnvironment(
        Dictionary<string, string> environment,
        ExternalProcessStreamingOptions? options)
    {
        if (!string.IsNullOrWhiteSpace(options?.ConnectionProtocol))
        {
            environment["BEACON_CONNECTION_PROTOCOL"] = options.ConnectionProtocol.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options?.ConnectionLaunchUri))
        {
            environment["BEACON_CONNECTION_LAUNCH_URI"] = options.ConnectionLaunchUri.Trim();
        }

        if (options?.ConnectionEndpoints is { Count: > 0 })
        {
            environment["BEACON_CONNECTION_ENDPOINTS"] = string.Join(
                ';',
                options.ConnectionEndpoints
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => $"{pair.Key.Trim()}={pair.Value.Trim()}"));
        }
    }

    private static StreamingConnectionDescriptor? CreateConnectionDescriptor(ExternalProcessStreamingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionProtocol)
            && string.IsNullOrWhiteSpace(options.ConnectionLaunchUri)
            && (options.ConnectionEndpoints is null || options.ConnectionEndpoints.Count == 0))
        {
            return null;
        }

        string protocol = string.IsNullOrWhiteSpace(options.ConnectionProtocol)
            ? "external-process"
            : options.ConnectionProtocol.Trim();
        IReadOnlyList<StreamingEndpointDescriptor> endpoints = (options.ConnectionEndpoints
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new StreamingEndpointDescriptor(pair.Key.Trim(), pair.Value.Trim()))
            .ToArray();

        return new StreamingConnectionDescriptor(
            protocol,
            string.IsNullOrWhiteSpace(options.ConnectionLaunchUri) ? null : options.ConnectionLaunchUri.Trim(),
            endpoints,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private ExternalStreamingManifestReadResult ReadManifestIfConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.ManifestPath))
        {
            return ExternalStreamingManifestReadResult.Ok(EmptyManifest);
        }

        string manifestPath = options.ManifestPath.Trim();
        if (!manifestReader.FileExists(manifestPath))
        {
            return ExternalStreamingManifestReadResult.Fail($"External streaming manifest '{manifestPath}' does not exist.");
        }

        return manifestReader.Read(manifestPath);
    }

    private static string? ValidateManifest(SessionPlan plan, ExternalStreamingManifest manifest)
    {
        if (ContainsValues(manifest.Codecs) && !Contains(manifest.Codecs, plan.Stream.Codec))
        {
            return $"External streaming manifest codec {plan.Stream.Codec} is not supported.";
        }

        if (manifest.MaxFps is > 0 && plan.Stream.Fps > manifest.MaxFps.Value)
        {
            return $"External streaming manifest supports up to {manifest.MaxFps.Value} FPS, but the plan requires {plan.Stream.Fps} FPS.";
        }

        if (manifest.MaxBitrateMbps is > 0 && plan.Stream.InitialBitrateMbps > manifest.MaxBitrateMbps.Value)
        {
            return $"External streaming manifest supports up to {manifest.MaxBitrateMbps.Value} Mbps, but the plan requires {plan.Stream.InitialBitrateMbps} Mbps.";
        }

        if (ContainsValues(manifest.Transports) && !Contains(manifest.Transports, plan.Stream.Transport))
        {
            return $"External streaming manifest transport {plan.Stream.Transport} is not supported.";
        }

        if (plan.Display.HdrEnabled && !manifest.Hdr10)
        {
            return "External streaming manifest does not support HDR10 required by the plan.";
        }

        return null;
    }

    private static bool ContainsValues(IReadOnlyList<string>? values) =>
        values is { Count: > 0 } && values.Any(value => !string.IsNullOrWhiteSpace(value));

    private static bool Contains(IReadOnlyList<string>? values, string expected) =>
        values?.Any(value => value.Equals(expected, StringComparison.OrdinalIgnoreCase)) == true;

    private sealed class NoExternalStreamingManifestReader : IExternalStreamingManifestReader
    {
        public static NoExternalStreamingManifestReader Instance { get; } = new();

        public bool FileExists(string path) => false;

        public ExternalStreamingManifestReadResult Read(string path) =>
            ExternalStreamingManifestReadResult.Fail($"External streaming manifest '{path}' does not exist.");
    }
}
