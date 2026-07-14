using Beacon.Core.Benchmarks;
using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Input;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.Streaming;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Worker.V1;
using System.Threading.Channels;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class StreamWorkerStreamingBackendTests
{
    [Fact]
    public async Task LegacyHostSupportsStableStartAndStopWithoutGenerationInterface()
    {
        var host = new LegacyRecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();

        StreamingStartResult start = await backend.StartAsync(plan, CancellationToken.None);
        StreamingStopResult stop = await backend.StopAsync(plan.SessionId, CancellationToken.None);

        Assert.True(start.Success, start.Error);
        Assert.True(stop.Success, stop.Error);
        Assert.Equal(
            [
                WorkerIpcEnvelope.BodyOneofCase.PrepareSession,
                WorkerIpcEnvelope.BodyOneofCase.StartMedia,
                WorkerIpcEnvelope.BodyOneofCase.StopMedia
            ],
            host.Commands.Select(command => command.BodyCase));
    }

    [Fact]
    public async Task LegacyHostIdentityChangeFailsStartTruthfully()
    {
        var host = new LegacyRecordingStreamWorkerHost
        {
            ReplaceAfter = WorkerIpcEnvelope.BodyOneofCase.PrepareSession
        };
        var backend = new StreamWorkerStreamingBackend(host);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Beacon StreamWorker generation changed during stream start.", result.Error);
        Assert.Empty(backend.GetSessions());
        Assert.False(host.MediaStarted);
    }

    [Fact]
    public async Task LegacyStartMediaReplacementIsShutDownBeforeGenerationFailure()
    {
        var host = new LegacyRecordingStreamWorkerHost
        {
            ReplaceAfter = WorkerIpcEnvelope.BodyOneofCase.StartMedia
        };
        var backend = new StreamWorkerStreamingBackend(host);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Beacon StreamWorker generation changed during stream start.", result.Error);
        Assert.Equal(1, host.ShutdownCalls);
        Assert.False(host.IsReady);
        Assert.False(host.MediaStarted);
        Assert.Empty(backend.GetSessions());
    }

    [Fact]
    public async Task WorkerExitBetweenPrepareAndStartDoesNotCreateOrUseReplacement()
    {
        var host = new RecordingStreamWorkerHost { ExitAfterPrepare = true };
        var backend = new StreamWorkerStreamingBackend(host);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Beacon StreamWorker generation changed during stream start.", result.Error);
        Assert.Equal(1, host.EnsureReadyCalls);
        Assert.Equal(
            [WorkerIpcEnvelope.BodyOneofCase.PrepareSession],
            host.GenerationBoundCommands.Select(command => command.BodyCase));
        Assert.Empty(backend.GetSessions());
        Assert.False(host.MediaStarted);
        Assert.False(host.IsReady);
    }

    [Fact]
    public async Task ProcessExitAfterFinalGenerationCheckCannotLeaveRunningSession()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        IStreamWorkerRuntimeEvents runtimeEvents = backend;
        host.ProcessGenerationCheckObserved = checkCount =>
        {
            if (checkCount == 2)
            {
                runtimeEvents.ProcessExited(new StreamWorkerProcessExited(1, 23));
            }
        };

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Beacon StreamWorker runtime changed during start_media.", result.Error);
        Assert.Empty(backend.GetSessions());
    }

    [Fact]
    public async Task AuthenticationBindsExactGenerationsAndResolvesInputRuntime()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();
        StreamingSessionState started = Assert.IsType<StreamingSessionState>(
            (await backend.StartAsync(plan, CancellationToken.None)).Session);
        IStreamWorkerRuntimeEvents runtimeEvents = backend;
        var authenticated = new StreamWorkerTransportAuthenticated(1, plan.SessionId, 7, 1200);

        bool bound = runtimeEvents.TryBind(authenticated);
        bool resolved = runtimeEvents.TryResolveInput(
            new StreamWorkerInputReceived(
                1,
                plan.SessionId,
                7,
                81,
                [ClientInputEvent.StreamKeyboard(0x1E, true)]),
            out ClientInputBatch? batch);

        Assert.True(bound);
        Assert.True(resolved);
        Assert.NotNull(batch);
        Assert.Equal(plan.ClientId.Value, batch.ClientId);
        Assert.Equal(plan.SessionId, batch.SessionId);
        Assert.Equal(plan.Display.DisplayId, batch.DisplayId);
        Assert.Equal(81, batch.Sequence);
        Assert.Equal(0x1Eu, Assert.Single(batch.Events).Keyboard?.ScanCode);
        Assert.Equal(started.RuntimeGeneration, runtimeEvents.GetBoundRuntimeGeneration(plan.SessionId));
    }

    [Fact]
    public async Task StaleProcessSessionAndServiceRuntimeEventsAreRejected()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();
        _ = await backend.StartAsync(plan, CancellationToken.None);
        IStreamWorkerRuntimeEvents runtimeEvents = backend;
        Assert.True(runtimeEvents.TryBind(
            new StreamWorkerTransportAuthenticated(1, plan.SessionId, 7, 1200)));

        Assert.False(runtimeEvents.TryResolveInput(
            new StreamWorkerInputReceived(2, plan.SessionId, 7, 1, [ClientInputEvent.StreamKeyboard(1, true)]),
            out _));
        Assert.False(runtimeEvents.TryResolveInput(
            new StreamWorkerInputReceived(1, plan.SessionId, 8, 1, [ClientInputEvent.StreamKeyboard(1, true)]),
            out _));
        Assert.False(runtimeEvents.IsCurrent(
            new StreamWorkerFeedbackReceived(1, "another", 7, 1, StreamWorkerFeedbackKind.Decoder, 1, 0, 0)));

        _ = await backend.StopAsync(plan.SessionId, CancellationToken.None);
        StreamingSessionState replacement = Assert.IsType<StreamingSessionState>(
            (await backend.StartAsync(plan, CancellationToken.None)).Session);

        Assert.False(runtimeEvents.TryResolveInput(
            new StreamWorkerInputReceived(1, plan.SessionId, 7, 2, [ClientInputEvent.StreamKeyboard(1, true)]),
            out _));
        Assert.NotEqual(Guid.Empty, replacement.RuntimeGeneration);
        Assert.Null(runtimeEvents.GetBoundRuntimeGeneration(plan.SessionId));
    }

    [Fact]
    public async Task TransportDisconnectClearsBindingButRetainsRuntimeForFreshGeneration()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();
        _ = await backend.StartAsync(plan, CancellationToken.None);
        IStreamWorkerRuntimeEvents runtimeEvents = backend;
        Assert.True(runtimeEvents.TryBind(
            new StreamWorkerTransportAuthenticated(1, plan.SessionId, 7, 1200)));

        Assert.True(runtimeEvents.IsCurrent(
            new StreamWorkerFeedbackReceived(1, plan.SessionId, 7, 1, StreamWorkerFeedbackKind.QueueDepth, 2, 3, 0)));
        Assert.True(runtimeEvents.IsCurrent(
            new StreamWorkerMediaEvidence(1, plan.SessionId, 7, 9, 10, 11)));
        Assert.False(runtimeEvents.TryDisconnect(
            new StreamWorkerTransportDisconnected(1, plan.SessionId, 8)));
        Assert.True(runtimeEvents.TryDisconnect(
            new StreamWorkerTransportDisconnected(1, plan.SessionId, 7)));
        Assert.False(runtimeEvents.IsCurrent(
            new StreamWorkerMediaEvidence(1, plan.SessionId, 7, 9, 10, 11)));
        StreamingSessionState retained = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(plan.SessionId, CancellationToken.None));
        Assert.Equal("running", retained.State);
        Assert.True(runtimeEvents.TryBind(
            new StreamWorkerTransportAuthenticated(1, plan.SessionId, 8, 1300)));
        Assert.True(runtimeEvents.IsCurrent(
            new StreamWorkerMediaEvidence(1, plan.SessionId, 8, 10, 11, 12)));
    }

    [Fact]
    public async Task ProcessExitImmediatelyInvalidatesOnlyOwnedRuntimes()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();
        _ = await backend.StartAsync(plan, CancellationToken.None);
        IStreamWorkerRuntimeEvents runtimeEvents = backend;
        Assert.True(runtimeEvents.TryBind(
            new StreamWorkerTransportAuthenticated(1, plan.SessionId, 7, 1200)));

        runtimeEvents.ProcessExited(new StreamWorkerProcessExited(1, 23));

        Assert.Null(await backend.GetSessionAsync(plan.SessionId, CancellationToken.None));
        Assert.False(runtimeEvents.TryResolveInput(
            new StreamWorkerInputReceived(1, plan.SessionId, 7, 1, [ClientInputEvent.StreamKeyboard(1, true)]),
            out _));

        host.ReplaceWorker([9, 8, 7]);
        _ = await backend.StartAsync(plan, CancellationToken.None);
        runtimeEvents.ProcessExited(new StreamWorkerProcessExited(1, 23));
        Assert.NotNull(await backend.GetSessionAsync(plan.SessionId, CancellationToken.None));
    }

    [Fact]
    public async Task ReplacementPrunesReplayHistoryWithoutAcceptingOldProcessEvents()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();
        IStreamWorkerRuntimeEvents runtimeEvents = backend;
        _ = await backend.StartAsync(plan, CancellationToken.None);
        Assert.True(runtimeEvents.TryBind(
            new StreamWorkerTransportAuthenticated(1, plan.SessionId, 7, 1200)));
        Assert.Equal(1, backend.RetainedWorkerGenerationHistoryCount);

        host.ReplaceWorker([9, 8, 7]);
        _ = await backend.StartAsync(plan, CancellationToken.None);

        Assert.Equal(0, backend.RetainedWorkerGenerationHistoryCount);
        Assert.False(runtimeEvents.TryBind(
            new StreamWorkerTransportAuthenticated(1, plan.SessionId, 8, 1200)));
        Assert.True(runtimeEvents.TryBind(
            new StreamWorkerTransportAuthenticated(2, plan.SessionId, 1, 1200)));
        Assert.Equal(1, backend.RetainedWorkerGenerationHistoryCount);
        Assert.True(runtimeEvents.TryDisconnect(
            new StreamWorkerTransportDisconnected(2, plan.SessionId, 1)));
        Assert.False(runtimeEvents.TryBind(
            new StreamWorkerTransportAuthenticated(2, plan.SessionId, 1, 1200)));
        Assert.Equal(1, backend.RetainedWorkerGenerationHistoryCount);
    }

    [Fact]
    public async Task FailedReplacementStartStillPrunesRetiredReplayHistory()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();
        IStreamWorkerRuntimeEvents runtimeEvents = backend;
        _ = await backend.StartAsync(plan, CancellationToken.None);
        Assert.True(runtimeEvents.TryBind(
            new StreamWorkerTransportAuthenticated(1, plan.SessionId, 7, 1200)));
        Assert.Equal(1, backend.RetainedWorkerGenerationHistoryCount);
        host.ReplaceWorker([9, 8, 7]);
        host.NextError = WorkerErrorCode.OperationFailed;

        StreamingStartResult replacement = await backend.StartAsync(plan, CancellationToken.None);

        Assert.False(replacement.Success);
        Assert.Equal(0, backend.RetainedWorkerGenerationHistoryCount);
    }

    [Fact]
    public async Task ProcessExitChurnLeavesNoReplayHistory()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();
        IStreamWorkerRuntimeEvents runtimeEvents = backend;

        for (int generation = 1; generation <= 32; generation++)
        {
            _ = await backend.StartAsync(plan, CancellationToken.None);
            Assert.True(runtimeEvents.TryBind(new StreamWorkerTransportAuthenticated(
                generation,
                plan.SessionId,
                checked((ulong)generation),
                1200)));
            Assert.Equal(1, backend.RetainedWorkerGenerationHistoryCount);

            runtimeEvents.ProcessExited(new StreamWorkerProcessExited(generation, 23));

            Assert.Equal(0, backend.RetainedWorkerGenerationHistoryCount);
            if (generation < 32)
            {
                host.ReplaceWorker([9, 8, checked((byte)generation)]);
            }
        }
    }
    [Fact]
    public async Task StartMapsPlanAndStopKeepsWorkerReady()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(
            host,
            new FixedDisplayNameResolver(@"\\.\DISPLAY7"));
        SessionPlan plan = CreatePlan();

        StreamingPreflightResult preflight = await backend.CheckReadinessAsync(plan, CancellationToken.None);
        StreamingStartResult start = await backend.StartAsync(plan, CancellationToken.None);
        StreamingStopResult stop = await backend.StopAsync(plan.SessionId, CancellationToken.None);

        Assert.True(preflight.Success);
        Assert.True(start.Success);
        Assert.Equal("running", start.Session?.State);
        Assert.Equal(51234, start.Session?.ActiveListenerPort);
        Assert.True(stop.Success);
        Assert.Equal("stopped", stop.Session?.State);
        Assert.Equal(3, host.Commands.Count);
        PrepareSession prepare = Assert.IsType<PrepareSession>(host.Commands[0].PrepareSession);
        Assert.Equal("Z Fold 7", host.Commands[0].SessionId);
        Assert.Equal("virtual-z-fold-7", prepare.DisplayTarget);
        Assert.Equal(@"\\.\DISPLAY7", prepare.DisplayDeviceName);
        Assert.Equal(2560u, prepare.Width);
        Assert.Equal(1600u, prepare.Height);
        Assert.Equal(120u, prepare.FramesPerSecondNumerator);
        Assert.Equal(WorkerVideoCodec.H264, prepare.VideoCodec);
        Assert.Equal(WorkerDynamicRange.Sdr, prepare.DynamicRange);
        Assert.Equal(45000u, prepare.InitialBitrateKbps);
        Assert.Equal(WorkerIpcEnvelope.BodyOneofCase.StartMedia, host.Commands[1].BodyCase);
        Assert.Equal("0.0.0.0", host.Commands[1].StartMedia.ListenAddress);
        Assert.Equal(0u, host.Commands[1].StartMedia.ListenPort);
        Assert.Equal(WorkerIpcEnvelope.BodyOneofCase.StopMedia, host.Commands[2].BodyCase);
        Assert.True(host.IsReady);
        Assert.Equal(0, host.ShutdownCalls);
    }

    [Fact]
    public async Task MissingWindowsDisplayMappingFailsBeforeWorkerReadiness()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(
            host,
            new FixedDisplayNameResolver(null));

        StreamingPreflightResult result = await backend.CheckReadinessAsync(
            CreatePlan(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Windows display target", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, host.EnsureReadyCalls);
        Assert.Empty(host.Commands);
    }

    [Fact]
    public async Task BenchmarkStartMapsExactPlanAndStopKeepsWorkerReady()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        IBenchmarkRuntime benchmarkRuntime = backend;
        var plan = new BenchmarkRuntimePlan(
            Guid.Parse("3c13df40-26c4-40c6-8414-268734f1024d"),
            new ClientId("z-fold-7"),
            BenchmarkTrigger.Manual,
            new BenchmarkTransportPlan(64, 65_536, 256, 1000, 1_000_000));
        host.StartMediaEvents[0].SessionId = plan.SessionId;

        BenchmarkRuntimeStartResult start = await benchmarkRuntime.StartAsync(
            plan,
            CancellationToken.None);
        BenchmarkRuntimeStopResult stop = await benchmarkRuntime.StopAsync(
            plan.SessionId,
            CancellationToken.None);

        BenchmarkRuntimeState started = Assert.IsType<BenchmarkRuntimeState>(start.Runtime);
        Assert.True(start.Success, start.Error);
        Assert.Equal("running", started.State);
        Assert.Equal(51234, started.ActiveListenerPort);
        Assert.Equal(plan.Revision, started.PlanRevision);
        Assert.Equal(16, started.RunToken.Length);
        Assert.True(stop.Success, stop.Error);
        Assert.Equal("stopped", stop.Runtime?.State);
        Assert.Equal(
            [
                WorkerIpcEnvelope.BodyOneofCase.PrepareBenchmark,
                WorkerIpcEnvelope.BodyOneofCase.StartMedia,
                WorkerIpcEnvelope.BodyOneofCase.StopMedia
            ],
            host.Commands.Select(command => command.BodyCase));
        PrepareBenchmark prepare = host.Commands[0].PrepareBenchmark;
        Assert.Equal(plan.RunId.ToString("D"), prepare.Plan.RunId);
        Assert.Equal((uint)plan.SchemaVersion, prepare.Plan.SchemaVersion);
        Assert.Equal(64u, prepare.Plan.ReliableRound.PacketCount);
        Assert.Equal(65_536u, prepare.Plan.ReliableRound.PayloadBytes);
        Assert.Equal(256u, prepare.Plan.DatagramRound.PacketCount);
        Assert.Equal(1000u, prepare.Plan.DatagramRound.PayloadBytes);
        Assert.Equal(1_000_000UL, prepare.Plan.DatagramRound.MeasurementIntervalUs);
        Assert.Equal(started.RunToken, prepare.Plan.RunToken.ToByteArray());
        Assert.True(host.IsReady);
        Assert.Equal(0, host.ShutdownCalls);
    }

    [Fact]
    public async Task BenchmarkRuntimeAcceptsOnlyItsBoundBenchmarkEvents()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        IBenchmarkRuntime benchmarkRuntime = backend;
        IStreamWorkerRuntimeEvents runtimeEvents = backend;
        var plan = new BenchmarkRuntimePlan(
            Guid.Parse("bcfb3bd7-e863-4e1d-b68f-86d694baf6c3"),
            new ClientId("z-fold-7"),
            BenchmarkTrigger.SessionPreflight,
            new BenchmarkTransportPlan(16, 32_768, 64, 1000, 250_000));
        host.StartMediaEvents[0].SessionId = plan.SessionId;
        Assert.True((await benchmarkRuntime.StartAsync(plan, CancellationToken.None)).Success);

        Assert.True(runtimeEvents.TryBind(new StreamWorkerTransportAuthenticated(
            ProcessGeneration: 1,
            plan.SessionId,
            WorkerSessionGeneration: 7,
            MaximumDatagramBytes: 1200)));
        Assert.True(runtimeEvents.IsCurrent(new StreamWorkerFeedbackReceived(
            ProcessGeneration: 1,
            plan.SessionId,
            WorkerSessionGeneration: 7,
            Sequence: 1,
            StreamWorkerFeedbackKind.Benchmark,
            PrimaryValue: 1000,
            SecondaryValue: 20,
            Count: 64)));
        Assert.False(runtimeEvents.IsCurrent(new StreamWorkerFeedbackReceived(
            ProcessGeneration: 1,
            plan.SessionId,
            WorkerSessionGeneration: 7,
            Sequence: 2,
            StreamWorkerFeedbackKind.Decoder,
            PrimaryValue: 0,
            SecondaryValue: 0,
            Count: 0)));
        Assert.True(runtimeEvents.TryDisconnect(new StreamWorkerTransportDisconnected(
            ProcessGeneration: 1,
            plan.SessionId,
            WorkerSessionGeneration: 7)));
    }

    [Fact]
    public async Task StartRejectsMissingTransportReadyEvent()
    {
        var host = new RecordingStreamWorkerHost();
        host.StartMediaEvents.Clear();
        var backend = new StreamWorkerStreamingBackend(host);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("exactly one valid transport-ready event", result.Error, StringComparison.Ordinal);
        Assert.Empty(backend.GetSessions());
    }

    [Fact]
    public async Task StartShutsDownWorkerWhenTransportReadyCleanupIsRejected()
    {
        var host = new RecordingStreamWorkerHost
        {
            StopMediaError = WorkerErrorCode.InvalidState,
        };
        host.StartMediaEvents.Clear();
        var backend = new StreamWorkerStreamingBackend(host);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(
            "StreamWorker start_media transport-ready validation failed; " +
            "stop_media cleanup was not confirmed and the Worker was shut down.",
            result.Error);
        Assert.Equal(1, host.ShutdownCalls);
        Assert.False(host.IsReady);
        Assert.Empty(backend.GetSessions());
    }

    [Fact]
    public async Task StartRejectsDuplicateTransportReadyEvents()
    {
        var host = new RecordingStreamWorkerHost();
        host.StartMediaEvents.Add(TransportReady(51235));
        var backend = new StreamWorkerStreamingBackend(host);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("exactly one valid transport-ready event", result.Error, StringComparison.Ordinal);
        Assert.Empty(backend.GetSessions());
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(65536u)]
    public async Task StartRejectsInvalidTransportReadyPort(uint port)
    {
        var host = new RecordingStreamWorkerHost();
        host.StartMediaEvents.Clear();
        host.StartMediaEvents.Add(TransportReady(port));
        var backend = new StreamWorkerStreamingBackend(host);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("exactly one valid transport-ready event", result.Error, StringComparison.Ordinal);
        Assert.Empty(backend.GetSessions());
    }

    [Fact]
    public async Task StartRejectsTransportReadyEventFromAnotherSession()
    {
        var host = new RecordingStreamWorkerHost();
        host.StartMediaEvents.Clear();
        WorkerIpcEnvelope transportReady = TransportReady(51234);
        transportReady.SessionId = "session-b";
        host.StartMediaEvents.Add(transportReady);
        var backend = new StreamWorkerStreamingBackend(host);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("exactly one valid transport-ready event", result.Error, StringComparison.Ordinal);
        Assert.Empty(backend.GetSessions());
    }

    [Fact]
    public async Task GetSessionInvalidatesCachedRunningStateAfterWorkerExit()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();
        Assert.True((await backend.StartAsync(plan, CancellationToken.None)).Success);
        host.MarkWorkerExited();

        StreamingSessionState? session = await backend.GetSessionAsync(
            plan.SessionId,
            CancellationToken.None);

        Assert.Null(session);
        Assert.DoesNotContain(backend.GetSessions(), value => value.State == "running");
    }

    [Fact]
    public async Task GetSessionInvalidatesCachedRunningStateAfterWorkerReplacement()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();
        Assert.True((await backend.StartAsync(plan, CancellationToken.None)).Success);
        host.ReplaceWorker([9, 8, 7]);

        StreamingSessionState? session = await backend.GetSessionAsync(
            plan.SessionId,
            CancellationToken.None);

        Assert.Null(session);
        Assert.DoesNotContain(backend.GetSessions(), value => value.State == "running");
    }

    [Fact]
    public Task GenerationAwareStopSerializesReplacementStart() =>
        AssertStopThenStartSerializedAsync(generationAware: true);

    [Fact]
    public Task ExplicitStopSerializesReplacementStart() =>
        AssertStopThenStartSerializedAsync(generationAware: false);

    private static async Task AssertStopThenStartSerializedAsync(bool generationAware)
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();
        StreamingSessionState runtimeA = Assert.IsType<StreamingSessionState>(
            (await backend.StartAsync(plan, CancellationToken.None)).Session);
        host.BlockNextStopMedia();

        Task<StreamingStopResult> stopRuntimeA = generationAware
            ? backend.StopRuntimeAsync(
                plan.SessionId,
                runtimeA.RuntimeGeneration,
                CancellationToken.None)
            : backend.StopAsync(plan.SessionId, CancellationToken.None);
        await host.StopMediaBlocked;
        int commandsBeforeReplacementStart = host.Commands.Count;

        Task<StreamingStartResult> startRuntimeB = backend.StartAsync(plan, CancellationToken.None);

        Assert.False(startRuntimeB.IsCompleted);
        Assert.Equal(commandsBeforeReplacementStart, host.Commands.Count);
        host.CompleteBlockedStopMedia();

        StreamingStopResult stopped = await stopRuntimeA;
        StreamingSessionState runtimeB = Assert.IsType<StreamingSessionState>(
            (await startRuntimeB).Session);
        StreamingSessionState current = Assert.IsType<StreamingSessionState>(
            await backend.GetSessionAsync(plan.SessionId, CancellationToken.None));
        Assert.NotEqual(runtimeA.RuntimeGeneration, runtimeB.RuntimeGeneration);
        Assert.True(stopped.Success);
        Assert.Equal(runtimeB.RuntimeGeneration, current.RuntimeGeneration);
        Assert.Equal("running", current.State);
        Assert.Equal(
            [
                WorkerIpcEnvelope.BodyOneofCase.PrepareSession,
                WorkerIpcEnvelope.BodyOneofCase.StartMedia,
                WorkerIpcEnvelope.BodyOneofCase.StopMedia,
                WorkerIpcEnvelope.BodyOneofCase.PrepareSession,
                WorkerIpcEnvelope.BodyOneofCase.StartMedia,
            ],
            host.Commands.Select(command => command.BodyCase));
        Assert.Equal(
            generationAware ? StopMediaReason.SessionFailed : StopMediaReason.Explicit,
            host.Commands[2].StopMedia.Reason);
    }

    [Fact]
    public async Task UnsupportedPlanFailsBeforeWorkerCommand()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan() with
        {
            Stream = CreatePlan().Stream with { Codec = "av1" },
        };

        StreamingPreflightResult result = await backend.CheckReadinessAsync(plan, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("h264", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(host.Commands);
    }

    [Fact]
    public async Task TypedWorkerFailureBecomesStableBackendError()
    {
        var host = new RecordingStreamWorkerHost
        {
            NextError = WorkerErrorCode.CapabilityUnavailable,
        };
        var backend = new StreamWorkerStreamingBackend(host);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("StreamWorker rejected prepare_session: capability_unavailable.", result.Error);
    }

    [Fact]
    public async Task HealthReportsWorkerExitWithoutThrowingOrRenderingProcessDetails()
    {
        var host = new RecordingStreamWorkerHost
        {
            ReadinessError = new StreamWorkerProcessExitedException(23),
        };
        var backend = new StreamWorkerStreamingBackend(host);

        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.False(health.Ready);
        Assert.Equal("unavailable", health.State);
        Assert.Equal("Beacon StreamWorker failed readiness verification.", health.Diagnostic);
        Assert.DoesNotContain("23", health.Diagnostic, StringComparison.Ordinal);
    }

    private static SessionPlan CreatePlan() => new(
        "Z Fold 7",
        new ClientId("z-fold-7"),
        "persona-5",
        new PlannedDisplay(
            "virtual-z-fold-7",
            2560,
            1600,
            120,
            "extended-primary",
            HdrPreference.Off,
            HdrEnabled: false,
            "sdr",
            "test"),
        new PlannedStream(
            "h264",
            120,
            45,
            "beacon-quic",
            "adaptive",
            "test",
            Guid.Parse("33acde60-b29f-4f03-b2b2-f51337bdb9a5"),
            "test-benchmark-revision"));

    private sealed class FixedDisplayNameResolver(string? displayName)
        : IWindowsDisplayNameResolver
    {
        public bool TryResolveDisplayName(string displayId, out string? resolved)
        {
            resolved = displayName;
            return resolved is not null;
        }
    }

    private static WorkerIpcEnvelope TransportReady(uint port) => new()
    {
        ProtocolVersion = ProtocolVersion.Current,
        RequestId = 42,
        SessionId = "Z Fold 7",
        WorkerTransportReady = new WorkerTransportReady { ListenerPort = port },
    };

    private sealed class RecordingStreamWorkerHost : IStreamWorkerHost, IGenerationBoundStreamWorkerHost
    {
        private readonly Channel<StreamWorkerEvent> events = Channel.CreateUnbounded<StreamWorkerEvent>();
        private byte[] workerInstanceId = [1, 2, 3];
        private TaskCompletionSource<StreamWorkerCommandResponse>? blockedStopCompletion;
        private TaskCompletionSource? stopMediaBlocked;
        private StreamWorkerCommandResponse? blockedStopResponse;

        public RecordingStreamWorkerHost()
        {
            StartMediaEvents.Add(TransportReady(51234));
        }

        public bool IsReady { get; private set; } = true;

        public ReadOnlyMemory<byte> WorkerInstanceId => workerInstanceId;

        public ChannelReader<StreamWorkerEvent> Events => events.Reader;

        public long CurrentProcessGeneration { get; private set; } = 1;

        public Action<int>? ProcessGenerationCheckObserved { get; set; }

        public int ProcessGenerationCheckCount { get; private set; }

        public bool IsCurrentProcessGeneration(long processGeneration)
        {
            bool isCurrent = processGeneration == CurrentProcessGeneration;
            ProcessGenerationCheckObserved?.Invoke(++ProcessGenerationCheckCount);
            return isCurrent;
        }

        public WorkerErrorCode NextError { get; set; } = WorkerErrorCode.None;

        public WorkerErrorCode StopMediaError { get; set; } = WorkerErrorCode.None;

        public int ShutdownCalls { get; private set; }

        public Exception? ReadinessError { get; set; }

        public bool ExitAfterPrepare { get; set; }

        public bool MediaStarted { get; private set; }

        public int EnsureReadyCalls { get; private set; }

        public List<WorkerIpcEnvelope> Commands { get; } = [];

        public List<WorkerIpcEnvelope> GenerationBoundCommands { get; } = [];

        public List<WorkerIpcEnvelope> StartMediaEvents { get; } = [];

        public Task StopMediaBlocked => stopMediaBlocked?.Task
            ?? throw new InvalidOperationException("No StopMedia command is blocked.");

        public void BlockNextStopMedia()
        {
            blockedStopCompletion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stopMediaBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void CompleteBlockedStopMedia()
        {
            StreamWorkerCommandResponse response = blockedStopResponse
                ?? throw new InvalidOperationException("No blocked StopMedia response is available.");
            blockedStopCompletion!.TrySetResult(response);
            blockedStopCompletion = null;
            stopMediaBlocked = null;
            blockedStopResponse = null;
        }

        public void MarkWorkerExited() => IsReady = false;

        public void ReplaceWorker(byte[] replacementWorkerInstanceId)
        {
            workerInstanceId = replacementWorkerInstanceId;
            CurrentProcessGeneration++;
            IsReady = true;
        }

        public Task EnsureReadyAsync(CancellationToken cancellationToken)
        {
            EnsureReadyCalls++;
            if (ReadinessError is not null)
            {
                return Task.FromException(ReadinessError);
            }
            IsReady = true;
            return Task.CompletedTask;
        }

        public Task<StreamWorkerCommandResponse> SendAsync(
            WorkerIpcEnvelope command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command.Clone());
            WorkerErrorCode responseError = command.BodyCase == WorkerIpcEnvelope.BodyOneofCase.StopMedia
                ? StopMediaError
                : NextError;
            var completion = new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                RequestId = 42,
                SessionId = command.SessionId,
                WorkerCompletion = new WorkerCompletion
                {
                    Succeeded = responseError == WorkerErrorCode.None,
                    ErrorCode = responseError,
                },
            };
            if (command.BodyCase == WorkerIpcEnvelope.BodyOneofCase.StopMedia)
            {
                StopMediaError = WorkerErrorCode.None;
            }
            else
            {
                NextError = WorkerErrorCode.None;
            }
            IReadOnlyList<WorkerIpcEnvelope> events = command.BodyCase == WorkerIpcEnvelope.BodyOneofCase.StartMedia
                ? StartMediaEvents.Select(value => value.Clone()).ToArray()
                : [];
            var response = new StreamWorkerCommandResponse(completion, events);
            if (command.BodyCase == WorkerIpcEnvelope.BodyOneofCase.ShutdownWorker)
            {
                ShutdownCalls++;
                IsReady = false;
            }
            if (command.BodyCase == WorkerIpcEnvelope.BodyOneofCase.StopMedia
                && blockedStopCompletion is not null)
            {
                blockedStopResponse = response;
                stopMediaBlocked!.TrySetResult();
                return blockedStopCompletion.Task;
            }
            return Task.FromResult(response);
        }

        public async Task<StreamWorkerCommandResponse> SendAsync(
            long expectedProcessGeneration,
            WorkerIpcEnvelope command,
            CancellationToken cancellationToken)
        {
            if (!IsReady || expectedProcessGeneration != CurrentProcessGeneration)
            {
                throw new StreamWorkerGenerationChangedException(expectedProcessGeneration);
            }
            GenerationBoundCommands.Add(command.Clone());
            StreamWorkerCommandResponse response = await SendAsync(command, cancellationToken);
            if (command.BodyCase == WorkerIpcEnvelope.BodyOneofCase.StartMedia)
            {
                MediaStarted = true;
            }
            if (ExitAfterPrepare
                && command.BodyCase == WorkerIpcEnvelope.BodyOneofCase.PrepareSession)
            {
                IsReady = false;
            }
            return response;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownCalls++;
            IsReady = false;
            return Task.CompletedTask;
        }
    }

    private sealed class LegacyRecordingStreamWorkerHost : IStreamWorkerHost
    {
        private byte[] workerInstanceId = [1, 2, 3];
        private ulong requestId;

        public bool IsReady { get; private set; } = true;

        public ReadOnlyMemory<byte> WorkerInstanceId => workerInstanceId;

        public WorkerIpcEnvelope.BodyOneofCase ReplaceAfter { get; init; }

        public bool MediaStarted { get; private set; }

        public int ShutdownCalls { get; private set; }

        public List<WorkerIpcEnvelope> Commands { get; } = [];

        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<StreamWorkerCommandResponse> SendAsync(
            WorkerIpcEnvelope command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command.Clone());
            ulong currentRequestId = ++requestId;
            IReadOnlyList<WorkerIpcEnvelope> events = [];
            if (command.BodyCase == WorkerIpcEnvelope.BodyOneofCase.StartMedia)
            {
                MediaStarted = true;
                WorkerIpcEnvelope transportReady = TransportReady(51234);
                transportReady.RequestId = currentRequestId;
                events = [transportReady];
            }
            if (command.BodyCase == ReplaceAfter)
            {
                workerInstanceId = [9, 8, 7];
            }
            return Task.FromResult(new StreamWorkerCommandResponse(
                new WorkerIpcEnvelope
                {
                    ProtocolVersion = ProtocolVersion.Current,
                    RequestId = currentRequestId,
                    SessionId = command.SessionId,
                    WorkerCompletion = new WorkerCompletion
                    {
                        Succeeded = true,
                        ErrorCode = WorkerErrorCode.None
                    }
                },
                events));
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownCalls++;
            IsReady = false;
            MediaStarted = false;
            return Task.CompletedTask;
        }
    }
}
