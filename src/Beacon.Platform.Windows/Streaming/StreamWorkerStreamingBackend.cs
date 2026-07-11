using System.Collections.Concurrent;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Platform.Windows.Streaming;

public sealed class StreamWorkerStreamingBackend(IStreamWorkerHost host) : IStreamingBackend
{
    private readonly ConcurrentDictionary<string, StreamingSessionState> sessions =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            await host.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            return new StreamingBackendHealth(
                Ready: host.IsReady,
                State: host.IsReady ? "ready" : "unavailable",
                Diagnostic: host.IsReady ? "Beacon StreamWorker ready." : "Beacon StreamWorker unavailable.",
                Capabilities: Capabilities(),
                ActiveSessions: sessions.Values.Count(session => session.State == "running"),
                Diagnostics: []);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new StreamingBackendHealth(
                Ready: false,
                State: "unavailable",
                Diagnostic: "Beacon StreamWorker failed readiness verification.",
                Capabilities: Capabilities(),
                ActiveSessions: sessions.Values.Count(session => session.State == "running"),
                Diagnostics: [error.GetType().Name]);
        }
    }

    public async Task<StreamingPreflightResult> CheckReadinessAsync(
        SessionPlan plan,
        CancellationToken cancellationToken)
    {
        string? invalid = ValidatePlan(plan);
        if (invalid is not null)
        {
            return StreamingPreflightResult.Fail(invalid);
        }
        try
        {
            await host.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            return host.IsReady
                ? StreamingPreflightResult.Ok()
                : StreamingPreflightResult.Fail("Beacon StreamWorker is not ready.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return StreamingPreflightResult.Fail(
                $"Beacon StreamWorker readiness failed ({error.GetType().Name}).");
        }
    }

    public async Task<StreamingStartResult> StartAsync(
        SessionPlan plan,
        CancellationToken cancellationToken)
    {
        string? invalid = ValidatePlan(plan);
        if (invalid is not null)
        {
            return StreamingStartResult.Fail(invalid);
        }

        await host.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        StreamWorkerCommandResponse prepared = await host.SendAsync(
            CreatePrepareCommand(plan),
            cancellationToken).ConfigureAwait(false);
        string? prepareError = CompletionError("prepare_session", prepared.Completion.WorkerCompletion);
        if (prepareError is not null)
        {
            return StreamingStartResult.Fail(prepareError);
        }

        StreamWorkerCommandResponse started = await host.SendAsync(
            new WorkerIpcEnvelope
            {
                SessionId = plan.SessionId,
                StartMedia = new StartMedia
                {
                    ListenAddress = "127.0.0.1",
                    ListenPort = 0,
                },
            },
            cancellationToken).ConfigureAwait(false);
        string? startError = CompletionError("start_media", started.Completion.WorkerCompletion);
        if (startError is not null)
        {
            return StreamingStartResult.Fail(startError);
        }

        var state = new StreamingSessionState(
            plan.SessionId,
            plan.ClientId.Value,
            plan.AppId,
            plan.Display.DisplayId,
            plan.Stream.Codec,
            plan.Stream.Fps,
            plan.Stream.InitialBitrateMbps,
            "running",
            null);
        sessions[plan.SessionId] = state;
        return StreamingStartResult.Ok(state);
    }

    public async Task<StreamingStopResult> StopAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(sessionId, out StreamingSessionState? session)
            || session.State != "running")
        {
            return StreamingStopResult.Fail($"Stream session '{sessionId}' is not running.");
        }

        StreamWorkerCommandResponse stopped = await host.SendAsync(
            new WorkerIpcEnvelope
            {
                SessionId = sessionId,
                StopMedia = new StopMedia { Reason = StopMediaReason.Explicit },
            },
            cancellationToken).ConfigureAwait(false);
        string? stopError = CompletionError("stop_media", stopped.Completion.WorkerCompletion);
        if (stopError is not null)
        {
            return StreamingStopResult.Fail(stopError);
        }

        StreamingSessionState state = session with { State = "stopped", Error = null };
        sessions[sessionId] = state;
        return StreamingStopResult.Ok(state);
    }

    public Task<StreamingSessionState?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(sessions.GetValueOrDefault(sessionId));

    public IReadOnlyList<StreamingSessionState> GetSessions() =>
        sessions.Values
            .OrderBy(session => session.ClientId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static StreamingCapabilities Capabilities() => new(
        Codecs: ["h264"],
        Encoders: ["fake"],
        CaptureMethods: ["fake"],
        MaxFps: 120,
        MaxBitrateMbps: null,
        Hdr10: false);

    private static string? ValidatePlan(SessionPlan plan)
    {
        if (!string.Equals(plan.Stream.Codec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            return "Beacon StreamWorker currently supports h264 only.";
        }
        if (plan.Display.HdrEnabled)
        {
            return "Beacon StreamWorker currently supports SDR only.";
        }
        return null;
    }

    private static WorkerIpcEnvelope CreatePrepareCommand(SessionPlan plan) => new()
    {
        SessionId = plan.SessionId,
        PrepareSession = new PrepareSession
        {
            DisplayTarget = plan.Display.DisplayId,
            VideoCodec = WorkerVideoCodec.H264,
            Width = checked((uint)plan.Display.Width),
            Height = checked((uint)plan.Display.Height),
            FramesPerSecondNumerator = checked((uint)plan.Stream.Fps),
            FramesPerSecondDenominator = 1,
            DynamicRange = WorkerDynamicRange.Sdr,
            MinimumBitrateKbps = checked((uint)Math.Max(1000, plan.Stream.InitialBitrateMbps * 500)),
            InitialBitrateKbps = checked((uint)plan.Stream.InitialBitrateMbps * 1000),
            MaximumBitrateKbps = checked((uint)plan.Stream.InitialBitrateMbps * 2000),
        },
    };

    private static string? CompletionError(string operation, WorkerCompletion completion) =>
        completion.Succeeded
            ? null
            : $"StreamWorker rejected {operation}: {ErrorName(completion.ErrorCode)}.";

    private static string ErrorName(WorkerErrorCode code) => code switch
    {
        WorkerErrorCode.UnsupportedVersion => "unsupported_version",
        WorkerErrorCode.InvalidRequest => "invalid_request",
        WorkerErrorCode.InvalidState => "invalid_state",
        WorkerErrorCode.CapabilityUnavailable => "capability_unavailable",
        WorkerErrorCode.OperationFailed => "operation_failed",
        _ => "unspecified",
    };
}
