using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Beacon.Core.Benchmarks;
using Beacon.Core.Input;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Displays;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Stream.V1;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Google.Protobuf;

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

public sealed record StreamWorkerRuntimeSnapshot(
    int RetainedStreams,
    int ActiveStreams,
    int RetainedBenchmarks,
    int ActiveBenchmarks,
    int BoundRuntimes);

public sealed class StreamWorkerStreamingBackend :
    IStreamingBackend,
    IBenchmarkRuntime,
    IStreamWorkerRuntimeEvents
{
    private readonly IStreamWorkerHost host;
    private readonly IWindowsDisplayNameResolver displayNames;
    private readonly ConcurrentDictionary<string, WorkerBoundStreamingSession> sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WorkerBoundBenchmarkRuntime> benchmarks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly Lock runtimeGate = new();
    private readonly Dictionary<(long ProcessGeneration, string SessionId), ulong> highestWorkerGenerations = [];
    private long latestExitedProcessGeneration;

    public StreamWorkerStreamingBackend(IStreamWorkerHost host)
        : this(host, new LogicalDisplayNameResolver())
    {
    }

    public StreamWorkerStreamingBackend(
        IStreamWorkerHost host,
        IWindowsDisplayNameResolver displayNames)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.displayNames = displayNames ?? throw new ArgumentNullException(nameof(displayNames));
    }

    internal int RetainedWorkerGenerationHistoryCount
    {
        get
        {
            lock (runtimeGate)
            {
                return highestWorkerGenerations.Count;
            }
        }
    }

    public Task<StreamingBackendHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!host.IsReady)
        {
            return Task.FromResult(new StreamingBackendHealth(
                Ready: true,
                State: "idle",
                Diagnostic: "Beacon StreamWorker is stopped and will start on demand.",
                Capabilities: Capabilities(host.Capabilities),
                ActiveSessions: GetSessions().Count(session => session.State == "running"),
                Diagnostics: []));
        }

        try
        {
            WorkerCapabilities workerCapabilities = host.Capabilities;
            byte[] workerInstanceId = host.WorkerInstanceId.ToArray();
            bool controlReady = host.IsReady
                && workerInstanceId.Length != 0
                && workerCapabilities.WorkerInstanceId.Span.SequenceEqual(workerInstanceId);
            bool ready = controlReady && workerCapabilities.VideoAvailable;
            return Task.FromResult(new StreamingBackendHealth(
                Ready: ready,
                State: ready ? "ready" : "unavailable",
                Diagnostic: ready
                    ? "Beacon StreamWorker ready."
                    : controlReady
                        ? VideoUnavailableError(workerCapabilities)
                        : "Beacon StreamWorker unavailable.",
                Capabilities: Capabilities(workerCapabilities),
                ActiveSessions: GetSessions().Count(session => session.State == "running"),
                Diagnostics: ready ? [] : VideoDiagnostics(workerCapabilities)));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return Task.FromResult(new StreamingBackendHealth(
                Ready: false,
                State: "unavailable",
                Diagnostic: "Beacon StreamWorker failed readiness verification.",
                Capabilities: Capabilities(host.Capabilities),
                ActiveSessions: GetSessions().Count(session => session.State == "running"),
                Diagnostics: [error.GetType().Name]));
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
        if (!TryResolveDisplayDeviceName(plan, out _))
        {
            return StreamingPreflightResult.Fail(
                $"Windows display target '{plan.Display.DisplayId}' is not mapped to an active device name.");
        }
        try
        {
            await host.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            WorkerCapabilities capabilities = host.Capabilities;
            long processGeneration = host.CurrentProcessGeneration;
            byte[] workerInstanceId = host.WorkerInstanceId.ToArray();
            if (!host.IsReady
                || workerInstanceId.Length == 0
                || !capabilities.WorkerInstanceId.Span.SequenceEqual(workerInstanceId)
                || !host.IsCurrentProcessGeneration(processGeneration))
            {
                return StreamingPreflightResult.Fail("Beacon StreamWorker is not ready.");
            }
            if (!capabilities.VideoAvailable)
            {
                return StreamingPreflightResult.Fail(VideoUnavailableError(capabilities));
            }
            return StreamingPreflightResult.Ok();
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
        if (!TryResolveDisplayDeviceName(plan, out string? displayDeviceName))
        {
            return StreamingStartResult.Fail(
                $"Windows display target '{plan.Display.DisplayId}' is not mapped to an active device name.");
        }

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await StartCoreAsync(plan, displayDeviceName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task<StreamingStartResult> StartCoreAsync(
        SessionPlan plan,
        string displayDeviceName,
        CancellationToken cancellationToken)
    {
        await host.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        WorkerCapabilities capabilities = host.Capabilities;
        long processGeneration = host.CurrentProcessGeneration;
        byte[] workerInstanceId = host.WorkerInstanceId.ToArray();
        if (!host.IsReady
            || workerInstanceId.Length == 0
            || !capabilities.WorkerInstanceId.Span.SequenceEqual(workerInstanceId)
            || !host.IsCurrentProcessGeneration(processGeneration))
        {
            return StreamingStartResult.Fail("Beacon StreamWorker runtime identity is unavailable.");
        }
        if (!capabilities.VideoAvailable)
        {
            return StreamingStartResult.Fail(VideoUnavailableError(capabilities));
        }
        lock (runtimeGate)
        {
            PruneWorkerGenerationHistoryBefore(processGeneration);
        }
        StreamWorkerCommandResponse prepared;
        try
        {
            prepared = await host.SendAsync(
                processGeneration,
                CreatePrepareCommand(plan, displayDeviceName),
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

        WorkerTransportStartResult transport = await StartTransportAsync(
            plan.SessionId,
            processGeneration,
            "Beacon StreamWorker generation changed during stream start.",
            cancellationToken).ConfigureAwait(false);
        if (!transport.Success)
        {
            return StreamingStartResult.Fail(transport.Error!);
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
            transport.ListenerPort,
            Guid.NewGuid());
        lock (runtimeGate)
        {
            bool isCurrentWorker = IsCurrentWorker(workerInstanceId, processGeneration);
            if (!isCurrentWorker || processGeneration <= latestExitedProcessGeneration)
            {
                return StreamingStartResult.Fail(
                    "Beacon StreamWorker runtime changed during start_media.");
            }
            sessions[plan.SessionId] = new WorkerBoundStreamingSession(
                state,
                workerInstanceId,
                processGeneration);
        }
        return StreamingStartResult.Ok(state);
    }

    async Task<BenchmarkRuntimeStartResult> IBenchmarkRuntime.StartAsync(
        BenchmarkRuntimePlan plan,
        CancellationToken cancellationToken)
    {
        string? invalid = ValidateBenchmarkPlan(plan);
        if (invalid is not null)
        {
            return BenchmarkRuntimeStartResult.Fail(invalid);
        }

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await StartBenchmarkCoreAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task<BenchmarkRuntimeStartResult> StartBenchmarkCoreAsync(
        BenchmarkRuntimePlan plan,
        CancellationToken cancellationToken)
    {
        await host.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        long processGeneration = host.CurrentProcessGeneration;
        byte[] workerInstanceId = host.WorkerInstanceId.ToArray();
        if (!host.IsReady
            || workerInstanceId.Length == 0
            || !host.IsCurrentProcessGeneration(processGeneration))
        {
            return BenchmarkRuntimeStartResult.Fail(
                "Beacon StreamWorker runtime identity is unavailable.");
        }

        lock (runtimeGate)
        {
            PruneWorkerGenerationHistoryBefore(processGeneration);
        }

        byte[] runToken = RandomNumberGenerator.GetBytes(16);
        StreamWorkerCommandResponse prepared;
        try
        {
            prepared = await host.SendAsync(
                processGeneration,
                CreatePrepareBenchmarkCommand(plan, runToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (IsGenerationFailure(error))
        {
            CryptographicOperations.ZeroMemory(runToken);
            return BenchmarkRuntimeStartResult.Fail(
                "Beacon StreamWorker generation changed during benchmark start.");
        }
        string? prepareError = CompletionError(
            "prepare_benchmark",
            prepared.Completion.WorkerCompletion);
        if (prepareError is not null)
        {
            CryptographicOperations.ZeroMemory(runToken);
            return BenchmarkRuntimeStartResult.Fail(prepareError);
        }

        WorkerTransportStartResult transport = await StartTransportAsync(
            plan.SessionId,
            processGeneration,
            "Beacon StreamWorker generation changed during benchmark start.",
            cancellationToken).ConfigureAwait(false);
        if (!transport.Success)
        {
            CryptographicOperations.ZeroMemory(runToken);
            return BenchmarkRuntimeStartResult.Fail(transport.Error!);
        }

        var state = new BenchmarkRuntimeState(
            plan.RunId,
            plan.SessionId,
            plan.ClientId.Value,
            plan.Revision,
            plan.SchemaVersion,
            plan.TransportPlan,
            runToken,
            State: "running",
            Error: null,
            transport.ListenerPort,
            Guid.NewGuid());
        lock (runtimeGate)
        {
            bool isCurrentWorker = IsCurrentWorker(workerInstanceId, processGeneration);
            if (!isCurrentWorker || processGeneration <= latestExitedProcessGeneration)
            {
                CryptographicOperations.ZeroMemory(runToken);
                return BenchmarkRuntimeStartResult.Fail(
                    "Beacon StreamWorker runtime changed during benchmark start_media.");
            }
            benchmarks[plan.SessionId] = new WorkerBoundBenchmarkRuntime(
                state with { RunToken = (byte[])runToken.Clone() },
                workerInstanceId,
                processGeneration);
        }
        return BenchmarkRuntimeStartResult.Started(state);
    }

    async Task<BenchmarkRuntimeStopResult> IBenchmarkRuntime.StopAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!benchmarks.TryGetValue(sessionId, out WorkerBoundBenchmarkRuntime? runtime))
            {
                return BenchmarkRuntimeStopResult.Fail(
                    $"Benchmark runtime '{sessionId}' is not running.");
            }
            if (runtime.State.State == "stopped")
            {
                return BenchmarkRuntimeStopResult.Stopped(runtime.State);
            }
            if (!IsCurrentWorker(runtime.WorkerInstanceId, runtime.ProcessGeneration))
            {
                RemoveBenchmarkIfCurrent(sessionId, runtime);
                return BenchmarkRuntimeStopResult.Fail(
                    $"Benchmark runtime '{sessionId}' Worker generation changed during stop.");
            }

            StreamWorkerCommandResponse stopped;
            try
            {
                stopped = await host.SendAsync(
                    runtime.ProcessGeneration,
                    new WorkerIpcEnvelope
                    {
                        SessionId = sessionId,
                        StopMedia = new StopMedia { Reason = StopMediaReason.Explicit },
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (IsGenerationFailure(error))
            {
                RemoveBenchmarkIfCurrent(sessionId, runtime);
                return BenchmarkRuntimeStopResult.Fail(
                    $"Benchmark runtime '{sessionId}' Worker generation changed during stop.");
            }
            string? stopError = CompletionError("stop_media", stopped.Completion.WorkerCompletion);
            if (stopError is not null)
            {
                return BenchmarkRuntimeStopResult.Fail(stopError);
            }

            lock (runtimeGate)
            {
                if (!benchmarks.TryGetValue(sessionId, out WorkerBoundBenchmarkRuntime? current)
                    || !ReferenceEquals(current, runtime))
                {
                    return BenchmarkRuntimeStopResult.Fail(
                        $"Benchmark runtime '{sessionId}' changed while stop_media was in flight.");
                }
                CryptographicOperations.ZeroMemory(runtime.State.RunToken);
                runtime.State = runtime.State with
                {
                    RunToken = [],
                    State = "stopped",
                    ActiveListenerPort = null,
                };
                runtime.Binding = null;
                return BenchmarkRuntimeStopResult.Stopped(runtime.State);
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    Task<BenchmarkRuntimeState?> IBenchmarkRuntime.GetAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!benchmarks.TryGetValue(sessionId, out WorkerBoundBenchmarkRuntime? runtime))
        {
            return Task.FromResult<BenchmarkRuntimeState?>(null);
        }
        if (runtime.State.State == "running"
            && !IsCurrentWorker(runtime.WorkerInstanceId, runtime.ProcessGeneration))
        {
            RemoveBenchmarkIfCurrent(sessionId, runtime);
            return Task.FromResult<BenchmarkRuntimeState?>(null);
        }
        return Task.FromResult<BenchmarkRuntimeState?>(runtime.State);
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
            stopped = await host.SendAsync(
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

    public StreamWorkerRuntimeSnapshot GetRuntimeSnapshot()
    {
        lock (runtimeGate)
        {
            return new StreamWorkerRuntimeSnapshot(
                sessions.Count,
                sessions.Values.Count(runtime => runtime.State.State == "running"),
                benchmarks.Count,
                benchmarks.Values.Count(runtime => runtime.State.State == "running"),
                sessions.Values.Count(runtime => runtime.Binding is not null)
                    + benchmarks.Values.Count(runtime => runtime.Binding is not null));
        }
    }

    public bool TryBind(StreamWorkerTransportAuthenticated authenticated)
    {
        lock (runtimeGate)
        {
            if (TryGetRunningRuntime(authenticated, out WorkerBoundStreamingSession? runtime))
            {
                if (!TryAcceptWorkerGeneration(
                        authenticated,
                        runtime.State.RuntimeGeneration,
                        runtime.Binding))
                {
                    return false;
                }
                runtime.Binding = new WorkerRuntimeBinding(
                    authenticated.ProcessGeneration,
                    authenticated.WorkerSessionGeneration,
                    runtime.State.RuntimeGeneration);
                return true;
            }
            if (!TryGetRunningBenchmarkRuntime(
                    authenticated,
                    out WorkerBoundBenchmarkRuntime? benchmark)
                || !TryAcceptWorkerGeneration(
                    authenticated,
                    benchmark.State.RuntimeGeneration,
                    benchmark.Binding))
            {
                return false;
            }
            benchmark.Binding = new WorkerRuntimeBinding(
                authenticated.ProcessGeneration,
                authenticated.WorkerSessionGeneration,
                benchmark.State.RuntimeGeneration);
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
            return feedback.Kind is StreamWorkerFeedbackKind.Benchmark
                or StreamWorkerFeedbackKind.BenchmarkDatagramEcho
                ? TryGetBoundBenchmarkRuntime(
                    feedback,
                    feedback.WorkerSessionGeneration,
                    out _)
                : TryGetBoundRuntime(
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
            if (TryGetBoundRuntime(
                    disconnected,
                    disconnected.WorkerSessionGeneration,
                    out WorkerBoundStreamingSession? runtime))
            {
                runtime.Binding = null;
                return true;
            }
            if (!TryGetBoundBenchmarkRuntime(
                    disconnected,
                    disconnected.WorkerSessionGeneration,
                    out WorkerBoundBenchmarkRuntime? benchmark))
            {
                return false;
            }
            benchmark.Binding = null;
            return true;
        }
    }

    public void ProcessExited(StreamWorkerProcessExited exited)
    {
        lock (runtimeGate)
        {
            latestExitedProcessGeneration = Math.Max(
                latestExitedProcessGeneration,
                exited.ProcessGeneration);
            RemoveWorkerGenerationHistory(exited.ProcessGeneration);
            foreach ((string sessionId, WorkerBoundStreamingSession runtime) in sessions.ToArray())
            {
                if (runtime.ProcessGeneration == exited.ProcessGeneration)
                {
                    RemoveIfCurrent(sessionId, runtime);
                }
            }
            foreach ((string sessionId, WorkerBoundBenchmarkRuntime runtime) in benchmarks.ToArray())
            {
                if (runtime.ProcessGeneration == exited.ProcessGeneration)
                {
                    RemoveBenchmarkIfCurrent(sessionId, runtime);
                }
            }
        }
    }

    public Guid? GetBoundRuntimeGeneration(string sessionId)
    {
        lock (runtimeGate)
        {
            if (sessions.TryGetValue(sessionId, out WorkerBoundStreamingSession? runtime))
            {
                return runtime.Binding?.RuntimeGeneration;
            }
            return benchmarks.TryGetValue(sessionId, out WorkerBoundBenchmarkRuntime? benchmark)
                ? benchmark.Binding?.RuntimeGeneration
                : null;
        }
    }

    private bool TryAcceptWorkerGeneration(
        StreamWorkerTransportAuthenticated authenticated,
        Guid runtimeGeneration,
        WorkerRuntimeBinding? existing)
    {
        PruneWorkerGenerationHistoryBefore(authenticated.ProcessGeneration);
        if (existing is not null
            && existing.ProcessGeneration == authenticated.ProcessGeneration
            && existing.WorkerSessionGeneration == authenticated.WorkerSessionGeneration
            && existing.RuntimeGeneration == runtimeGeneration)
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
        return true;
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
            && host.IsCurrentProcessGeneration(workerEvent.ProcessGeneration)
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

    private bool TryGetRunningBenchmarkRuntime(
        StreamWorkerEvent workerEvent,
        [NotNullWhen(true)] out WorkerBoundBenchmarkRuntime? runtime)
    {
        runtime = null;
        return workerEvent.SessionId is not null
            && benchmarks.TryGetValue(workerEvent.SessionId, out runtime)
            && runtime.State.State == "running"
            && runtime.ProcessGeneration == workerEvent.ProcessGeneration
            && host.IsReady
            && host.IsCurrentProcessGeneration(workerEvent.ProcessGeneration)
            && runtime.State.RuntimeGeneration != Guid.Empty;
    }

    private bool TryGetBoundBenchmarkRuntime(
        StreamWorkerEvent workerEvent,
        ulong workerSessionGeneration,
        [NotNullWhen(true)] out WorkerBoundBenchmarkRuntime? runtime)
    {
        if (!TryGetRunningBenchmarkRuntime(workerEvent, out runtime) || runtime.Binding is null)
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
            && host.IsCurrentProcessGeneration(processGeneration)
            && !currentWorkerInstanceId.IsEmpty
            && currentWorkerInstanceId.Span.SequenceEqual(workerInstanceId);
    }

    private void RemoveIfCurrent(string sessionId, WorkerBoundStreamingSession runtime) =>
        ((ICollection<KeyValuePair<string, WorkerBoundStreamingSession>>)sessions)
            .Remove(new KeyValuePair<string, WorkerBoundStreamingSession>(sessionId, runtime));

    private void RemoveBenchmarkIfCurrent(string sessionId, WorkerBoundBenchmarkRuntime runtime)
    {
        if (((ICollection<KeyValuePair<string, WorkerBoundBenchmarkRuntime>>)benchmarks)
            .Remove(new KeyValuePair<string, WorkerBoundBenchmarkRuntime>(sessionId, runtime)))
        {
            CryptographicOperations.ZeroMemory(runtime.State.RunToken);
        }
    }

    private void PruneWorkerGenerationHistoryBefore(long processGeneration)
    {
        foreach ((long retiredGeneration, string sessionId) in highestWorkerGenerations.Keys
            .Where(key => key.ProcessGeneration < processGeneration)
            .ToArray())
        {
            highestWorkerGenerations.Remove((retiredGeneration, sessionId));
        }
    }

    private void RemoveWorkerGenerationHistory(long processGeneration)
    {
        foreach ((long exitedGeneration, string sessionId) in highestWorkerGenerations.Keys
            .Where(key => key.ProcessGeneration == processGeneration)
            .ToArray())
        {
            highestWorkerGenerations.Remove((exitedGeneration, sessionId));
        }
    }

    private async Task<WorkerTransportStartResult> StartTransportAsync(
        string sessionId,
        long processGeneration,
        string generationFailureError,
        CancellationToken cancellationToken)
    {
        StreamWorkerCommandResponse started;
        try
        {
            started = await host.SendAsync(
                processGeneration,
                new WorkerIpcEnvelope
                {
                    SessionId = sessionId,
                    StartMedia = new StartMedia
                    {
                        ListenAddress = "0.0.0.0",
                        ListenPort = 0,
                    },
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException cancellation)
        {
            string? cleanupError = await CleanupUncertainTransportStartAsync(
                sessionId,
                processGeneration).ConfigureAwait(false);
            if (cleanupError is not null)
            {
                throw new InvalidOperationException(cleanupError, cancellation);
            }
            throw;
        }
        catch (Exception error) when (IsGenerationFailure(error))
        {
            return WorkerTransportStartResult.Fail(generationFailureError);
        }
        catch (Exception failure)
        {
            string? cleanupError = await CleanupUncertainTransportStartAsync(
                sessionId,
                processGeneration).ConfigureAwait(false);
            if (cleanupError is not null)
            {
                throw new InvalidOperationException(cleanupError, failure);
            }
            throw;
        }
        string? startError = CompletionError("start_media", started.Completion.WorkerCompletion);
        if (startError is not null)
        {
            return WorkerTransportStartResult.Fail(startError);
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
            || !string.Equals(transportReady.SessionId, sessionId, StringComparison.Ordinal)
            || transportReady.WorkerTransportReady.ListenerPort is 0 or > 65_535)
        {
            string? cleanupError = await CleanupInvalidTransportReadyAsync(
                sessionId,
                processGeneration,
                cancellationToken).ConfigureAwait(false);
            return WorkerTransportStartResult.Fail(
                cleanupError
                ?? "StreamWorker start_media requires exactly one valid transport-ready event.");
        }

        return WorkerTransportStartResult.Started(
            checked((int)transportReady.WorkerTransportReady.ListenerPort));
    }

    private async Task<string?> CleanupUncertainTransportStartAsync(
        string sessionId,
        long processGeneration)
    {
        try
        {
            StreamWorkerCommandResponse stopped = await host.SendAsync(
                processGeneration,
                new WorkerIpcEnvelope
                {
                    SessionId = sessionId,
                    StopMedia = new StopMedia { Reason = StopMediaReason.SessionFailed },
                },
                CancellationToken.None).ConfigureAwait(false);
            if (IsSuccessfulCompletion(stopped.Completion, sessionId))
            {
                return null;
            }
        }
        catch (Exception error) when (IsGenerationFailure(error))
        {
            return null;
        }
        catch (Exception)
        {
        }

        try
        {
            await ShutdownGenerationAsync(processGeneration).ConfigureAwait(false);
            return null;
        }
        catch (Exception error) when (IsGenerationFailure(error))
        {
            return null;
        }
        catch (Exception error)
        {
            return "StreamWorker start_media cancellation cleanup was not confirmed " +
                $"({error.GetType().Name}).";
        }
    }

    private async Task<string?> CleanupInvalidTransportReadyAsync(
        string sessionId,
        long processGeneration,
        CancellationToken cancellationToken)
    {
        try
        {
            StreamWorkerCommandResponse stopped = await host.SendAsync(
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
        StreamWorkerCommandResponse response = await host.SendAsync(
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

    private static StreamingCapabilities Capabilities(WorkerCapabilities value) => new(
        Codecs: value.VideoCodecs.Contains(WorkerVideoCodec.H264) ? ["h264"] : [],
        Encoders: value.VideoEncoders.Contains(WorkerVideoEncoder.Nvenc) ? ["nvenc"] : [],
        CaptureMethods: value.CaptureMethods.Contains(WorkerCaptureMethod.WindowsGraphicsCapture)
            ? ["wgc"]
            : [],
        MaxFps: checked((int)value.MaximumFramesPerSecond),
        MaxBitrateMbps: value.MaximumBitrateKbps == 0
            ? null
            : checked((int)Math.Ceiling(value.MaximumBitrateKbps / 1000d)),
        Hdr10: value.Hdr10);

    private static string VideoUnavailableError(WorkerCapabilities value) =>
        $"Beacon StreamWorker production video is unavailable " +
        $"({value.VideoUnavailableBoundary.ToString().ToLowerInvariant()}:{value.VideoUnavailableCode}).";

    private static IReadOnlyList<string> VideoDiagnostics(WorkerCapabilities value) =>
        value.VideoAvailable || value.VideoUnavailableCode == 0
            ? []
            : [$"{value.VideoUnavailableBoundary.ToString().ToLowerInvariant()}:{value.VideoUnavailableCode}"];

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
        if (plan.Stream.Width <= 0 || plan.Stream.Height <= 0)
        {
            return "Beacon StreamWorker requires a positive benchmark-certified stream mode.";
        }
        return null;
    }

    private static string? ValidateBenchmarkPlan(BenchmarkRuntimePlan plan)
    {
        BenchmarkTransportPlan transport = plan.TransportPlan;
        return plan.RunId == Guid.Empty
            || string.IsNullOrWhiteSpace(plan.ClientId.Value)
            || plan.SchemaVersion <= 0
            || transport.ReliablePacketCount <= 0
            || transport.ReliablePayloadBytes <= 0
            || transport.DatagramPacketCount <= 0
            || transport.DatagramPayloadBytes <= 0
            || transport.MeasurementIntervalUs <= 0
            ? "Beacon benchmark runtime plan is invalid."
            : null;
    }

    private static WorkerIpcEnvelope CreatePrepareCommand(
        SessionPlan plan,
        string displayDeviceName) => new()
        {
            SessionId = plan.SessionId,
            PrepareSession = new PrepareSession
            {
                DisplayTarget = plan.Display.DisplayId,
                DisplayDeviceName = displayDeviceName,
                VideoCodec = WorkerVideoCodec.H264,
                Width = checked((uint)plan.Stream.Width),
                Height = checked((uint)plan.Stream.Height),
                FramesPerSecondNumerator = checked((uint)plan.Stream.Fps),
                FramesPerSecondDenominator = 1,
                DynamicRange = WorkerDynamicRange.Sdr,
                MinimumBitrateKbps = checked((uint)Math.Max(1000, plan.Stream.InitialBitrateMbps * 500)),
                InitialBitrateKbps = checked((uint)plan.Stream.InitialBitrateMbps * 1000),
                MaximumBitrateKbps = checked((uint)plan.Stream.InitialBitrateMbps * 2000),
                AudioCodec = WorkerAudioCodec.Opus,
                AudioSampleRateHz = checked((uint)plan.Audio.SampleRateHz),
                AudioChannelCount = checked((uint)plan.Audio.ChannelCount),
                AudioFrameDurationUs = checked((uint)plan.Audio.FrameDurationUs),
                AudioBitrateBps = checked((uint)plan.Audio.BitrateBps),
            },
        };

    private bool TryResolveDisplayDeviceName(
        SessionPlan plan,
        [NotNullWhen(true)] out string? displayDeviceName)
    {
        if (!displayNames.TryResolveDisplayName(
                plan.Display.DisplayId,
                out displayDeviceName)
            || string.IsNullOrWhiteSpace(displayDeviceName))
        {
            displayDeviceName = null;
            return false;
        }
        return true;
    }

    private sealed class LogicalDisplayNameResolver : IWindowsDisplayNameResolver
    {
        public bool TryResolveDisplayName(string displayId, out string? displayName)
        {
            displayName = displayId;
            return !string.IsNullOrWhiteSpace(displayName);
        }
    }

    private static WorkerIpcEnvelope CreatePrepareBenchmarkCommand(
        BenchmarkRuntimePlan plan,
        byte[] runToken) => new()
        {
            SessionId = plan.SessionId,
            PrepareBenchmark = new PrepareBenchmark
            {
                Plan = new StartBenchmark
                {
                    RunId = plan.RunId.ToString("D"),
                    SchemaVersion = checked((uint)plan.SchemaVersion),
                    ReliableRound = new BenchmarkRoundPlan
                    {
                        PacketCount = checked((uint)plan.TransportPlan.ReliablePacketCount),
                        PayloadBytes = checked((uint)plan.TransportPlan.ReliablePayloadBytes),
                        MeasurementIntervalUs = checked((ulong)plan.TransportPlan.MeasurementIntervalUs),
                    },
                    DatagramRound = new BenchmarkRoundPlan
                    {
                        PacketCount = checked((uint)plan.TransportPlan.DatagramPacketCount),
                        PayloadBytes = checked((uint)plan.TransportPlan.DatagramPayloadBytes),
                        MeasurementIntervalUs = checked((ulong)plan.TransportPlan.MeasurementIntervalUs),
                    },
                    RunToken = ByteString.CopyFrom(runToken),
                },
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

    private sealed record WorkerTransportStartResult(
        bool Success,
        int ListenerPort,
        string? Error)
    {
        public static WorkerTransportStartResult Started(int listenerPort) =>
            new(true, listenerPort, null);

        public static WorkerTransportStartResult Fail(string error) =>
            new(false, 0, error);
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

    private sealed class WorkerBoundBenchmarkRuntime(
        BenchmarkRuntimeState state,
        byte[] workerInstanceId,
        long processGeneration)
    {
        public BenchmarkRuntimeState State { get; set; } = state;

        public byte[] WorkerInstanceId { get; } = workerInstanceId;

        public long ProcessGeneration { get; } = processGeneration;

        public WorkerRuntimeBinding? Binding { get; set; }
    }

    private sealed record WorkerRuntimeBinding(
        long ProcessGeneration,
        ulong WorkerSessionGeneration,
        Guid RuntimeGeneration);
}
