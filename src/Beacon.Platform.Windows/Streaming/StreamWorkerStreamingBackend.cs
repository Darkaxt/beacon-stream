using System.Collections.Concurrent;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Platform.Windows.Streaming;

public sealed class StreamWorkerStreamingBackend(IStreamWorkerHost host) : IStreamingBackend
{
    private readonly ConcurrentDictionary<string, WorkerBoundStreamingSession> sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);

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
                ActiveSessions: GetSessions().Count(session => session.State == "running"),
                Diagnostics: []);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new StreamingBackendHealth(
                Ready: false,
                State: "unavailable",
                Diagnostic: "Beacon StreamWorker failed readiness verification.",
                Capabilities: Capabilities(),
                ActiveSessions: GetSessions().Count(session => session.State == "running"),
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

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await StartCoreAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task<StreamingStartResult> StartCoreAsync(
        SessionPlan plan,
        CancellationToken cancellationToken)
    {
        await host.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        byte[] workerInstanceId = host.WorkerInstanceId.ToArray();
        if (!host.IsReady || workerInstanceId.Length == 0)
        {
            return StreamingStartResult.Fail("Beacon StreamWorker runtime identity is unavailable.");
        }
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
                    ListenAddress = "0.0.0.0",
                    ListenPort = 0,
                },
            },
            cancellationToken).ConfigureAwait(false);
        string? startError = CompletionError("start_media", started.Completion.WorkerCompletion);
        if (startError is not null)
        {
            return StreamingStartResult.Fail(startError);
        }

        WorkerIpcEnvelope[] transportReadyEvents = started.Events
            .Where(value => value.BodyCase == WorkerIpcEnvelope.BodyOneofCase.WorkerTransportReady)
            .ToArray();
        WorkerIpcEnvelope? transportReady = transportReadyEvents.Length == 1
            ? transportReadyEvents[0]
            : null;
        if (transportReady is null
            || transportReady.ProtocolVersion != ProtocolVersion.Current
            || transportReady.RequestId == 0
            || transportReady.RequestId != started.Completion.RequestId
            || !string.Equals(transportReady.SessionId, plan.SessionId, StringComparison.Ordinal)
            || transportReady.WorkerTransportReady.ListenerPort is 0 or > 65_535)
        {
            string? cleanupError = await CleanupInvalidTransportReadyAsync(
                plan.SessionId,
                cancellationToken).ConfigureAwait(false);
            return StreamingStartResult.Fail(
                cleanupError
                ?? "StreamWorker start_media requires exactly one valid transport-ready event.");
        }
        if (!IsCurrentWorker(workerInstanceId))
        {
            return StreamingStartResult.Fail(
                "Beacon StreamWorker runtime changed during start_media.");
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
            null,
            checked((int)transportReady.WorkerTransportReady.ListenerPort),
            Guid.NewGuid());
        sessions[plan.SessionId] = new WorkerBoundStreamingSession(state, workerInstanceId);
        return StreamingStartResult.Ok(state);
    }

    public async Task<StreamingStopResult> StopAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        await StopCoreAsync(
            sessionId,
            expectedGeneration: null,
            StopMediaReason.Explicit,
            cancellationToken).ConfigureAwait(false);

    public async Task<StreamingStopResult> StopRuntimeAsync(
        string sessionId,
        Guid expectedGeneration,
        CancellationToken cancellationToken) =>
        await StopCoreAsync(
            sessionId,
            expectedGeneration,
            StopMediaReason.SessionFailed,
            cancellationToken).ConfigureAwait(false);

    private async Task<StreamingStopResult> StopCoreAsync(
        string sessionId,
        Guid? expectedGeneration,
        StopMediaReason reason,
        CancellationToken cancellationToken)
    {
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await StopUnderGateAsync(
                sessionId,
                expectedGeneration,
                reason,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task<StreamingStopResult> StopUnderGateAsync(
        string sessionId,
        Guid? expectedGeneration,
        StopMediaReason reason,
        CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(sessionId, out WorkerBoundStreamingSession? runtime)
            || runtime.State.State != "running"
            || !IsCurrentWorker(runtime.WorkerInstanceId))
        {
            if (runtime is not null)
            {
                RemoveIfCurrent(sessionId, runtime);
            }
            return StreamingStopResult.Fail($"Stream session '{sessionId}' is not running.");
        }
        if (expectedGeneration.HasValue
            && runtime.State.RuntimeGeneration != expectedGeneration.Value)
        {
            return StreamingStopResult.Fail(
                $"Stream session '{sessionId}' runtime generation changed before compensation.");
        }

        StreamWorkerCommandResponse stopped = await host.SendAsync(
            new WorkerIpcEnvelope
            {
                SessionId = sessionId,
                StopMedia = new StopMedia { Reason = reason },
            },
            cancellationToken).ConfigureAwait(false);
        string? stopError = CompletionError("stop_media", stopped.Completion.WorkerCompletion);
        if (stopError is not null)
        {
            return StreamingStopResult.Fail(stopError);
        }

        StreamingSessionState state = runtime.State with
        {
            State = "stopped",
            Error = null,
            ActiveListenerPort = null,
        };
        if (!sessions.TryUpdate(sessionId, runtime with { State = state }, runtime))
        {
            return StreamingStopResult.Fail(
                $"Stream session '{sessionId}' runtime changed while stop_media was in flight.");
        }
        return StreamingStopResult.Ok(state);
    }

    public Task<StreamingSessionState?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(sessionId, out WorkerBoundStreamingSession? runtime))
        {
            return Task.FromResult<StreamingSessionState?>(null);
        }
        if (runtime.State.State == "running" && !IsCurrentWorker(runtime.WorkerInstanceId))
        {
            RemoveIfCurrent(sessionId, runtime);
            return Task.FromResult<StreamingSessionState?>(null);
        }

        return Task.FromResult<StreamingSessionState?>(runtime.State);
    }

    public IReadOnlyList<StreamingSessionState> GetSessions()
    {
        foreach ((string sessionId, WorkerBoundStreamingSession runtime) in sessions.ToArray())
        {
            if (runtime.State.State == "running" && !IsCurrentWorker(runtime.WorkerInstanceId))
            {
                RemoveIfCurrent(sessionId, runtime);
            }
        }

        return sessions.Values
            .Select(runtime => runtime.State)
            .OrderBy(session => session.ClientId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool IsCurrentWorker(byte[] workerInstanceId)
    {
        ReadOnlyMemory<byte> currentWorkerInstanceId = host.WorkerInstanceId;
        return host.IsReady
            && !currentWorkerInstanceId.IsEmpty
            && currentWorkerInstanceId.Span.SequenceEqual(workerInstanceId);
    }

    private void RemoveIfCurrent(string sessionId, WorkerBoundStreamingSession runtime) =>
        ((ICollection<KeyValuePair<string, WorkerBoundStreamingSession>>)sessions)
            .Remove(new KeyValuePair<string, WorkerBoundStreamingSession>(sessionId, runtime));

    private async Task<string?> CleanupInvalidTransportReadyAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            StreamWorkerCommandResponse stopped = await host.SendAsync(
                new WorkerIpcEnvelope
                {
                    SessionId = sessionId,
                    StopMedia = new StopMedia { Reason = StopMediaReason.SessionFailed },
                },
                cancellationToken).ConfigureAwait(false);
            if (IsSuccessfulCompletion(stopped.Completion, sessionId))
            {
                return null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await host.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
        }

        try
        {
            await host.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            return "StreamWorker start_media transport-ready validation failed; " +
                "stop_media cleanup was not confirmed and the Worker was shut down.";
        }
        catch (Exception)
        {
            return "StreamWorker start_media transport-ready validation failed; " +
                "stop_media cleanup was not confirmed and Worker shutdown failed.";
        }
    }

    private static bool IsSuccessfulCompletion(WorkerIpcEnvelope completion, string sessionId) =>
        completion.ProtocolVersion == ProtocolVersion.Current
        && completion.RequestId != 0
        && string.Equals(completion.SessionId, sessionId, StringComparison.Ordinal)
        && completion.BodyCase == WorkerIpcEnvelope.BodyOneofCase.WorkerCompletion
        && completion.WorkerCompletion.Succeeded
        && completion.WorkerCompletion.ErrorCode == WorkerErrorCode.None;

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

    private sealed record WorkerBoundStreamingSession(
        StreamingSessionState State,
        byte[] WorkerInstanceId);
}
