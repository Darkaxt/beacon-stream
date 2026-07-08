using System.Globalization;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;

namespace Beacon.Platform.Windows.Streaming;

public sealed record ExternalProcessStreamingOptions(string? ExecutablePath);

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

public sealed class ExternalProcessStreamingBackend(
    ExternalProcessStreamingOptions options,
    IExternalStreamingProcessRunner runner) : IStreamingBackend
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, StreamingSessionState> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExternalStreamingProcess> processes = new(StringComparer.OrdinalIgnoreCase);

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
            ExternalStreamingCommand command = CreateStartCommand(options.ExecutablePath!, plan);
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
                Connection: null);

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

    public static ExternalStreamingCommand CreateStartCommand(string executablePath, SessionPlan plan)
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

        string arguments = $"--session \"{plan.SessionId}\" --display \"{plan.Display.DisplayId}\"";
        return new ExternalStreamingCommand(executablePath, arguments, environment);
    }
}
