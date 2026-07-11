using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Beacon.Core.Input;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Platform.Windows.Streaming;

public interface IStreamWorkerRuntimeEvents
{
    bool TryBind(StreamWorkerTransportAuthenticated authenticated);

    bool TryResolveInput(StreamWorkerInputReceived input, out ClientInputBatch? batch);

    bool IsCurrent(StreamWorkerFeedbackReceived feedback);

    bool IsCurrent(StreamWorkerMediaEvidence media);

    bool TryDisconnect(StreamWorkerTransportDisconnected disconnected);

    void ProcessExited(StreamWorkerProcessExited exited);

    Guid? GetBoundRuntimeGeneration(string sessionId);
}

public sealed class StreamWorkerStreamingBackend : IStreamingBackend, IStreamWorkerRuntimeEvents
{
    private readonly IStreamWorkerHost host;
    private readonly IGenerationBoundStreamWorkerHost generationHost;
    private readonly ConcurrentDictionary<string, WorkerBoundStreamingSession> sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly Lock runtimeGate = new();
    private readonly Dictionary<(long ProcessGeneration, string SessionId), ulong> highestWorkerGenerations = [];

    public StreamWorkerStreamingBackend(IStreamWorkerHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        generationHost = host as IGenerationBoundStreamWorkerHost
            ?? new LegacyGenerationBoundStreamWorkerHost(host);
    }

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
        long processGeneration = generationHost.CurrentProcessGeneration;
        byte[] workerInstanceId = host.WorkerInstanceId.ToArray();
        if (!host.IsReady
            || workerInstanceId.Length == 0
            || !generationHost.IsCurrentProcessGeneration(processGeneration))
        {
            return StreamingStartResult.Fail("Beacon StreamWorker runtime identity is unavailable.");
        }
        StreamWorkerCommandResponse prepared;
        try
        {
            prepared = await generationHost.SendAsync(
                processGeneration,
                CreatePrepareCommand(plan),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (IsGenerationFailure(error))
        {
            return StreamingStartResult.Fail("Beacon StreamWorker generation changed during stream start.");
        }
        string? prepareError = CompletionError("prepare_session", prepared.Completion.WorkerCompletion);
        if (prepareError is not null)
        {
            return StreamingStartResult.Fail(prepareError);
        }

        StreamWorkerCommandResponse started;
        try
        {
            started = await generationHost.SendAsync(
                processGeneration,
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
        }
        catch (Exception error) when (IsGenerationFailure(error))
        {
            return StreamingStartResult.Fail("Beacon StreamWorker generation changed during stream start.");
        }
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
                processGeneration,
                cancellationToken).ConfigureAwait(false);
            return StreamingStartResult.Fail(
                cleanupError
                ?? "StreamWorker start_media requires exactly one valid transport-ready event.");
        }
        if (!IsCurrentWorker(workerInstanceId, processGeneration))
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
        lock (runtimeGate)
        {
            sessions[plan.SessionId] = new WorkerBoundStreamingSession(
                state,
                workerInstanceId,
                processGeneration);
        }
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
            || !IsCurrentWorker(runtime.WorkerInstanceId, runtime.ProcessGeneration))
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

        StreamWorkerCommandResponse stopped;
        try
        {
            stopped = await generationHost.SendAsync(
                runtime.ProcessGeneration,
                new WorkerIpcEnvelope
                {
                    SessionId = sessionId,
                    StopMedia = new StopMedia { Reason = reason },
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (IsGenerationFailure(error))
        {
            RemoveIfCurrent(sessionId, runtime);
            return StreamingStopResult.Fail(
                $"Stream session '{sessionId}' Worker generation changed during stop.");
        }
        string? stopError = CompletionError("stop_media", stopped.Completion.WorkerCompletion);
        if (stopError is not null)
        {
            return StreamingStopResult.Fail(stopError);
        }

        StreamingSessionState state;
        lock (runtimeGate)
        {
            if (!sessions.TryGetValue(sessionId, out WorkerBoundStreamingSession? current)
                || !ReferenceEquals(current, runtime))
            {
                return StreamingStopResult.Fail(
                    $"Stream session '{sessionId}' runtime changed while stop_media was in flight.");
            }
            state = runtime.State with
            {
                State = "stopped",
                Error = null,
                ActiveListenerPort = null,
            };
            runtime.State = state;
            runtime.Binding = null;
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
        if (runtime.State.State == "running"
            && !IsCurrentWorker(runtime.WorkerInstanceId, runtime.ProcessGeneration))
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
            if (runtime.State.State == "running"
                && !IsCurrentWorker(runtime.WorkerInstanceId, runtime.ProcessGeneration))
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

    public bool TryBind(StreamWorkerTransportAuthenticated authenticated)
    {
        lock (runtimeGate)
        {
            if (!TryGetRunningRuntime(authenticated, out WorkerBoundStreamingSession? runtime))
            {
                return false;
            }
            if (runtime.Binding is { } existing
                && existing.ProcessGeneration == authenticated.ProcessGeneration
                && existing.WorkerSessionGeneration == authenticated.WorkerSessionGeneration
                && existing.RuntimeGeneration == runtime.State.RuntimeGeneration)
            {
                return true;
            }

            var key = (authenticated.ProcessGeneration, authenticated.SessionId!.ToUpperInvariant());
            if (highestWorkerGenerations.TryGetValue(key, out ulong highest)
                && authenticated.WorkerSessionGeneration <= highest)
            {
                return false;
            }
            highestWorkerGenerations[key] = authenticated.WorkerSessionGeneration;
            runtime.Binding = new WorkerRuntimeBinding(
                authenticated.ProcessGeneration,
                authenticated.WorkerSessionGeneration,
                runtime.State.RuntimeGeneration);
            return true;
        }
    }

    public bool TryResolveInput(StreamWorkerInputReceived input, out ClientInputBatch? batch)
    {
        lock (runtimeGate)
        {
            if (input.Sequence > long.MaxValue
                || input.Events.Count == 0
                || !TryGetBoundRuntime(input, input.WorkerSessionGeneration, out WorkerBoundStreamingSession? runtime))
            {
                batch = null;
                return false;
            }
            batch = new ClientInputBatch(
                runtime.State.ClientId,
                runtime.State.SessionId,
                runtime.State.DisplayId,
                checked((long)input.Sequence),
                input.Events);
            return true;
        }
    }

    public bool IsCurrent(StreamWorkerFeedbackReceived feedback)
    {
        lock (runtimeGate)
        {
            return TryGetBoundRuntime(
                feedback,
                feedback.WorkerSessionGeneration,
                out _);
        }
    }

    public bool IsCurrent(StreamWorkerMediaEvidence media)
    {
        lock (runtimeGate)
        {
            return TryGetBoundRuntime(media, media.WorkerSessionGeneration, out _);
        }
    }

    public bool TryDisconnect(StreamWorkerTransportDisconnected disconnected)
    {
        lock (runtimeGate)
        {
            if (!TryGetBoundRuntime(
                    disconnected,
                    disconnected.WorkerSessionGeneration,
                    out WorkerBoundStreamingSession? runtime))
            {
                return false;
            }
            runtime.Binding = null;
            return true;
        }
    }

    public void ProcessExited(StreamWorkerProcessExited exited)
    {
        lock (runtimeGate)
        {
            foreach ((string sessionId, WorkerBoundStreamingSession runtime) in sessions.ToArray())
            {
                if (runtime.ProcessGeneration == exited.ProcessGeneration)
                {
                    RemoveIfCurrent(sessionId, runtime);
                }
            }
        }
    }

    public Guid? GetBoundRuntimeGeneration(string sessionId)
    {
        lock (runtimeGate)
        {
            return sessions.TryGetValue(sessionId, out WorkerBoundStreamingSession? runtime)
                ? runtime.Binding?.RuntimeGeneration
                : null;
        }
    }

    private bool TryGetRunningRuntime(
        StreamWorkerEvent workerEvent,
        [NotNullWhen(true)] out WorkerBoundStreamingSession? runtime)
    {
        runtime = null;
        return workerEvent.SessionId is not null
            && sessions.TryGetValue(workerEvent.SessionId, out runtime)
            && runtime.State.State == "running"
            && runtime.ProcessGeneration == workerEvent.ProcessGeneration
            && host.IsReady
            && generationHost.IsCurrentProcessGeneration(workerEvent.ProcessGeneration)
            && runtime.State.RuntimeGeneration != Guid.Empty;
    }

    private bool TryGetBoundRuntime(
        StreamWorkerEvent workerEvent,
        ulong workerSessionGeneration,
        [NotNullWhen(true)] out WorkerBoundStreamingSession? runtime)
    {
        if (!TryGetRunningRuntime(workerEvent, out runtime) || runtime.Binding is null)
        {
            return false;
        }
        WorkerRuntimeBinding binding = runtime.Binding;
        return binding.ProcessGeneration == workerEvent.ProcessGeneration
            && binding.WorkerSessionGeneration == workerSessionGeneration
            && binding.RuntimeGeneration == runtime.State.RuntimeGeneration;
    }

    private bool IsCurrentWorker(byte[] workerInstanceId, long processGeneration)
    {
        ReadOnlyMemory<byte> currentWorkerInstanceId = host.WorkerInstanceId;
        return host.IsReady
            && generationHost.IsCurrentProcessGeneration(processGeneration)
            && !currentWorkerInstanceId.IsEmpty
            && currentWorkerInstanceId.Span.SequenceEqual(workerInstanceId);
    }

    private void RemoveIfCurrent(string sessionId, WorkerBoundStreamingSession runtime) =>
        ((ICollection<KeyValuePair<string, WorkerBoundStreamingSession>>)sessions)
            .Remove(new KeyValuePair<string, WorkerBoundStreamingSession>(sessionId, runtime));

    private async Task<string?> CleanupInvalidTransportReadyAsync(
        string sessionId,
        long processGeneration,
        CancellationToken cancellationToken)
    {
        try
        {
            StreamWorkerCommandResponse stopped = await generationHost.SendAsync(
                processGeneration,
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
            await ShutdownGenerationAsync(processGeneration).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
        }

        try
        {
            await ShutdownGenerationAsync(processGeneration).ConfigureAwait(false);
            return "StreamWorker start_media transport-ready validation failed; " +
                "stop_media cleanup was not confirmed and the Worker was shut down.";
        }
        catch (Exception)
        {
            return "StreamWorker start_media transport-ready validation failed; " +
                "stop_media cleanup was not confirmed and Worker shutdown failed.";
        }
    }

    private async Task ShutdownGenerationAsync(long processGeneration)
    {
        StreamWorkerCommandResponse response = await generationHost.SendAsync(
            processGeneration,
            new WorkerIpcEnvelope { ShutdownWorker = new ShutdownWorker() },
            CancellationToken.None).ConfigureAwait(false);
        if (!response.Completion.WorkerCompletion.Succeeded)
        {
            throw new StreamWorkerProtocolException("StreamWorker rejected generation shutdown.");
        }
    }

    private static bool IsGenerationFailure(Exception error) =>
        error is StreamWorkerGenerationChangedException or StreamWorkerProcessExitedException;

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

    private sealed class LegacyGenerationBoundStreamWorkerHost : IGenerationBoundStreamWorkerHost
    {
        private readonly IStreamWorkerHost host;
        private readonly Channel<StreamWorkerEvent> events = Channel.CreateBounded<StreamWorkerEvent>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
        private readonly SemaphoreSlim commandGate = new(1, 1);
        private readonly Lock identityGate = new();
        private byte[] workerInstanceId = [];
        private long processGeneration;

        public LegacyGenerationBoundStreamWorkerHost(IStreamWorkerHost host)
        {
            this.host = host;
        }

        public ChannelReader<StreamWorkerEvent> Events => events.Reader;

        public long CurrentProcessGeneration => CaptureCurrentGeneration();

        public bool IsCurrentProcessGeneration(long expectedProcessGeneration)
        {
            if (!host.IsReady || expectedProcessGeneration <= 0)
            {
                return false;
            }
            byte[] currentIdentity = host.WorkerInstanceId.ToArray();
            lock (identityGate)
            {
                return expectedProcessGeneration == processGeneration
                    && currentIdentity.Length > 0
                    && currentIdentity.AsSpan().SequenceEqual(workerInstanceId);
            }
        }

        public async Task<StreamWorkerCommandResponse> SendAsync(
            long expectedProcessGeneration,
            WorkerIpcEnvelope command,
            CancellationToken cancellationToken)
        {
            await commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[] before = host.WorkerInstanceId.ToArray();
                long currentGeneration = CaptureGeneration(before);
                if (!host.IsReady
                    || before.Length == 0
                    || currentGeneration != expectedProcessGeneration)
                {
                    throw new StreamWorkerGenerationChangedException(expectedProcessGeneration);
                }

                StreamWorkerCommandResponse response;
                try
                {
                    response = await host.SendAsync(command, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await FailIfGenerationChangedAsync(before, expectedProcessGeneration).ConfigureAwait(false);
                    throw;
                }
                await FailIfGenerationChangedAsync(before, expectedProcessGeneration).ConfigureAwait(false);
                return response;
            }
            finally
            {
                commandGate.Release();
            }
        }

        private long CaptureCurrentGeneration()
        {
            if (!host.IsReady)
            {
                return 0;
            }
            return CaptureGeneration(host.WorkerInstanceId.ToArray());
        }

        private long CaptureGeneration(byte[] identity)
        {
            if (identity.Length == 0)
            {
                return 0;
            }
            lock (identityGate)
            {
                if (!identity.AsSpan().SequenceEqual(workerInstanceId))
                {
                    workerInstanceId = identity;
                    processGeneration++;
                }
                return processGeneration;
            }
        }

        private async Task FailIfGenerationChangedAsync(
            byte[] before,
            long expectedProcessGeneration)
        {
            byte[] after = host.WorkerInstanceId.ToArray();
            bool identityChanged = !after.AsSpan().SequenceEqual(before);
            if (identityChanged)
            {
                if (after.Length > 0)
                {
                    _ = CaptureGeneration(after);
                }
                try
                {
                    await host.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    throw new StreamWorkerGenerationChangedException(expectedProcessGeneration);
                }
            }
            if (!host.IsReady)
            {
                throw new StreamWorkerGenerationChangedException(expectedProcessGeneration);
            }
        }
    }

    private sealed class WorkerBoundStreamingSession(
        StreamingSessionState state,
        byte[] workerInstanceId,
        long processGeneration)
    {
        public StreamingSessionState State { get; set; } = state;

        public byte[] WorkerInstanceId { get; } = workerInstanceId;

        public long ProcessGeneration { get; } = processGeneration;

        public WorkerRuntimeBinding? Binding { get; set; }
    }

    private sealed record WorkerRuntimeBinding(
        long ProcessGeneration,
        ulong WorkerSessionGeneration,
        Guid RuntimeGeneration);
}
