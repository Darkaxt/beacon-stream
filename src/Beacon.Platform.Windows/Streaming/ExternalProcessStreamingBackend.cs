using System.Globalization;
using Beacon.Core.Diagnostics;
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

public sealed record ExternalStreamingProcessStatus(bool IsRunning, long? ExitCode, string? Diagnostic)
{
    public static ExternalStreamingProcessStatus Running() => new(true, null, null);

    public static ExternalStreamingProcessStatus Exited(long? exitCode) =>
        new(false, exitCode, exitCode.HasValue
            ? $"External streaming process exited with code {exitCode.Value}."
            : "External streaming process is not running.");

    public static ExternalStreamingProcessStatus Unknown(string diagnostic) => new(false, null, diagnostic);
}

public sealed record ExternalStreamingProcessStopResult(bool Success, string? Error)
{
    public static ExternalStreamingProcessStopResult Ok() => new(true, null);

    public static ExternalStreamingProcessStopResult Fail(string error) => new(false, error);
}

public interface IExternalStreamingProcessRunner
{
    bool FileExists(string path);

    ExternalStreamingProcess Start(ExternalStreamingCommand command);

    ExternalStreamingProcessStatus GetStatus(ExternalStreamingProcess process);

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
    IExternalStreamingManifestReader? manifestReader = null,
    IDiagnosticEventSink? diagnostics = null) : IStreamingBackend
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
    private readonly List<string> processDiagnostics = [];
    private readonly IExternalStreamingManifestReader manifestReader = manifestReader ?? NoExternalStreamingManifestReader.Instance;

    public Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReconcileProcessStates();

        string? executablePath = TrimOrNull(options.ExecutablePath);
        bool executableConfigured = executablePath is not null;
        bool executableAvailable = false;
        bool manifestConfigured = !string.IsNullOrWhiteSpace(options.ManifestPath);
        bool manifestAvailable = false;
        string? diagnostic = null;
        ExternalStreamingManifest? manifest = null;

        try
        {
            if (!executableConfigured)
            {
                diagnostic = "External streaming executable path is not configured. Set Beacon:Streaming:ExternalProcess:ExecutablePath or BEACON_EXTERNAL_STREAMING_EXECUTABLE.";
            }
            else
            {
                executableAvailable = runner.FileExists(executablePath!);
                if (!executableAvailable)
                {
                    diagnostic = $"External streaming executable '{executablePath}' does not exist.";
                }
            }

            if (manifestConfigured)
            {
                string manifestPath = options.ManifestPath!.Trim();
                manifestAvailable = manifestReader.FileExists(manifestPath);
                if (!manifestAvailable)
                {
                    diagnostic ??= $"External streaming manifest '{manifestPath}' does not exist.";
                }
                else
                {
                    ExternalStreamingManifestReadResult read = manifestReader.Read(manifestPath);
                    if (!read.Success)
                    {
                        diagnostic ??= read.Error ?? "External streaming manifest is invalid.";
                    }
                    else
                    {
                        manifest = read.Manifest;
                        if (manifest is null)
                        {
                            diagnostic ??= "External streaming manifest is empty.";
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostic = $"External streaming health check failed: {ex.Message}";
        }

        int activeSessions;
        IReadOnlyList<string> processDiagnosticSnapshot;
        lock (gate)
        {
            activeSessions = sessions.Values.Count(session =>
                session.State.Equals("running", StringComparison.OrdinalIgnoreCase));
            processDiagnosticSnapshot = processDiagnostics.ToArray();
        }

        bool ready = executableConfigured
            && executableAvailable
            && (!manifestConfigured || (manifestAvailable && manifest is not null))
            && diagnostic is null;

        StreamingBackendHealth health = new(
            Ready: ready,
            Backend: "external-process",
            Diagnostic: diagnostic ?? "External streaming backend ready.",
            ExecutableConfigured: executableConfigured,
            ExecutableAvailable: executableAvailable,
            ExecutablePath: executablePath,
            ManifestConfigured: manifestConfigured,
            ManifestAvailable: manifestAvailable,
            ManifestPath: TrimOrNull(options.ManifestPath),
            ManifestName: TrimOrNull(manifest?.Name),
            Protocol: TrimOrNull(options.ConnectionProtocol) ?? TrimOrNull(manifest?.Protocol),
            LaunchUri: TrimOrNull(options.ConnectionLaunchUri) ?? TrimOrNull(manifest?.LaunchUri),
            Codecs: NormalizeList(manifest?.Codecs),
            Transports: NormalizeList(manifest?.Transports),
            Encoders: NormalizeList(manifest?.Encoders),
            Capture: NormalizeList(manifest?.Capture),
            MaxFps: manifest?.MaxFps,
            MaxBitrateMbps: manifest?.MaxBitrateMbps,
            Hdr10: manifest?.Hdr10 == true,
            ActiveSessions: activeSessions,
            Diagnostics: NormalizeList(manifest?.Diagnostics)
                .Concat(processDiagnosticSnapshot)
                .ToArray());
        return Task.FromResult(health);
    }

    public Task<StreamingPreflightResult> CheckReadinessAsync(SessionPlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            return PreflightFailure(
                plan,
                "External streaming executable path is not configured. Set Beacon:Streaming:ExternalProcess:ExecutablePath or BEACON_EXTERNAL_STREAMING_EXECUTABLE.");
        }

        if (!runner.FileExists(options.ExecutablePath))
        {
            return PreflightFailure(
                plan,
                $"External streaming executable '{options.ExecutablePath}' does not exist.");
        }

        ExternalStreamingManifestReadResult manifest = ReadManifestIfConfigured();
        if (!manifest.Success)
        {
            return PreflightFailure(plan, manifest.Error ?? "External streaming manifest is invalid.");
        }

        if (manifest.Manifest is not null)
        {
            string? compatibilityError = ValidateManifest(plan, manifest.Manifest);
            if (!string.IsNullOrWhiteSpace(compatibilityError))
            {
                return PreflightFailure(plan, compatibilityError);
            }
        }

        Publish(plan, "preflight", DiagnosticSeverity.Information, "External streaming backend preflight passed.");
        return Task.FromResult(StreamingPreflightResult.Ok());
    }

    public async Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken)
    {
        StreamingPreflightResult preflight = await CheckReadinessAsync(plan, cancellationToken);
        if (!preflight.Success)
        {
            return StreamingStartResult.Fail(preflight.Error ?? "External streaming backend is not ready.");
        }

        ExternalStreamingManifestReadResult manifestResult = ReadManifestIfConfigured();
        if (!manifestResult.Success)
        {
            return StreamingStartResult.Fail(manifestResult.Error ?? "External streaming manifest is invalid.");
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
                CreateConnectionDescriptor(options, manifestResult.Manifest));

            lock (gate)
            {
                sessions[plan.SessionId] = session;
                processes[plan.SessionId] = process;
            }

            Publish(plan, "start", DiagnosticSeverity.Information, "External streaming process started.");
            return StreamingStartResult.Ok(session);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            Publish(plan, "start", DiagnosticSeverity.Error, $"External streaming process failed to start: {ex.Message}");
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
                Publish(session, "stop", DiagnosticSeverity.Error, stop.Error ?? $"External streaming process {process.ProcessId} did not stop.");
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

        Publish(stopped, "stop", DiagnosticSeverity.Information, "External streaming process stopped.");
        return Task.FromResult(StreamingStopResult.Ok(stopped));
    }

    public Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReconcileProcessStates();
        lock (gate)
        {
            return Task.FromResult(sessions.GetValueOrDefault(sessionId));
        }
    }

    public IReadOnlyList<StreamingSessionState> GetSessions()
    {
        ReconcileProcessStates();
        lock (gate)
        {
            return sessions.Values
                .OrderBy(session => session.ClientId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private IReadOnlyList<string> ReconcileProcessStates()
    {
        var reconciledDiagnostics = new List<string>();
        var exitedEvents = new List<(StreamingSessionState Session, string Error)>();
        lock (gate)
        {
            foreach ((string sessionId, ExternalStreamingProcess process) in processes.ToArray())
            {
                if (!sessions.TryGetValue(sessionId, out StreamingSessionState? session)
                    || !session.State.Equals("running", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ExternalStreamingProcessStatus status = runner.GetStatus(process);
                if (status.IsRunning)
                {
                    continue;
                }

                string error = status.Diagnostic ?? $"External streaming process {process.ProcessId} exited.";
                if (status.ExitCode.HasValue && !error.Contains(status.ExitCode.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                {
                    error = $"{error} ExitCode={status.ExitCode.Value}.";
                }

                StreamingSessionState exited = session with { State = "exited", Error = error };
                sessions[sessionId] = exited;
                processes.Remove(sessionId);
                string diagnostic = $"{sessionId}: {error}";
                processDiagnostics.Add(diagnostic);
                reconciledDiagnostics.Add(diagnostic);
                exitedEvents.Add((exited, error));
            }
        }

        foreach ((StreamingSessionState session, string error) in exitedEvents)
        {
            Publish(session, "process-exited", DiagnosticSeverity.Error, error);
        }

        return reconciledDiagnostics;
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

        if (!string.IsNullOrWhiteSpace(options?.ManifestPath))
        {
            environment["BEACON_WRAPPER_MANIFEST_PATH"] = options.ManifestPath.Trim();
        }
    }

    private static StreamingConnectionDescriptor? CreateConnectionDescriptor(
        ExternalProcessStreamingOptions options,
        ExternalStreamingManifest? manifest)
    {
        string? protocolSource = string.IsNullOrWhiteSpace(options.ConnectionProtocol)
            ? manifest?.Protocol
            : options.ConnectionProtocol;
        string? launchUriSource = string.IsNullOrWhiteSpace(options.ConnectionLaunchUri)
            ? manifest?.LaunchUri
            : options.ConnectionLaunchUri;
        IReadOnlyDictionary<string, string>? endpointSource = options.ConnectionEndpoints is { Count: > 0 }
            ? options.ConnectionEndpoints
            : manifest?.Endpoints;

        if (string.IsNullOrWhiteSpace(protocolSource)
            && string.IsNullOrWhiteSpace(launchUriSource)
            && (endpointSource is null || endpointSource.Count == 0))
        {
            return null;
        }

        string protocol = string.IsNullOrWhiteSpace(protocolSource)
            ? "external-process"
            : protocolSource.Trim();
        IReadOnlyList<StreamingEndpointDescriptor> endpoints = (endpointSource
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new StreamingEndpointDescriptor(pair.Key.Trim(), pair.Value.Trim()))
            .ToArray();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(options.ManifestPath))
        {
            metadata["manifestPath"] = options.ManifestPath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(manifest?.Name))
        {
            metadata["manifestName"] = manifest.Name.Trim();
        }

        return new StreamingConnectionDescriptor(
            protocol,
            string.IsNullOrWhiteSpace(launchUriSource) ? null : launchUriSource.Trim(),
            endpoints,
            metadata);
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

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray()
        ?? [];

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Task<StreamingPreflightResult> PreflightFailure(SessionPlan plan, string message)
    {
        Publish(plan, "preflight", DiagnosticSeverity.Error, message);
        return Task.FromResult(StreamingPreflightResult.Fail(message));
    }

    private void Publish(SessionPlan plan, string operation, string severity, string message)
    {
        diagnostics?.Publish(DiagnosticEvent.Create(
            severity,
            "streaming",
            operation,
            message,
            plan.ClientId.Value,
            plan.SessionId,
            plan.Display.DisplayId,
            StreamingMetadata(plan)));
    }

    private void Publish(StreamingSessionState session, string operation, string severity, string message)
    {
        diagnostics?.Publish(DiagnosticEvent.Create(
            severity,
            "streaming",
            operation,
            message,
            session.ClientId,
            session.SessionId,
            session.DisplayId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["codec"] = session.Codec,
                ["fps"] = session.Fps.ToString(CultureInfo.InvariantCulture),
                ["bitrateMbps"] = session.InitialBitrateMbps.ToString(CultureInfo.InvariantCulture),
                ["transport"] = session.Transport,
                ["state"] = session.State
            }));
    }

    private Dictionary<string, string> StreamingMetadata(SessionPlan plan)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appId"] = plan.AppId,
            ["codec"] = plan.Stream.Codec,
            ["fps"] = plan.Stream.Fps.ToString(CultureInfo.InvariantCulture),
            ["bitrateMbps"] = plan.Stream.InitialBitrateMbps.ToString(CultureInfo.InvariantCulture),
            ["transport"] = plan.Stream.Transport
        };

        if (!string.IsNullOrWhiteSpace(options.ManifestPath))
        {
            metadata["manifestPath"] = options.ManifestPath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            metadata["executablePath"] = options.ExecutablePath.Trim();
        }

        return metadata;
    }

    private sealed class NoExternalStreamingManifestReader : IExternalStreamingManifestReader
    {
        public static NoExternalStreamingManifestReader Instance { get; } = new();

        public bool FileExists(string path) => false;

        public ExternalStreamingManifestReadResult Read(string path) =>
            ExternalStreamingManifestReadResult.Fail($"External streaming manifest '{path}' does not exist.");
    }
}
