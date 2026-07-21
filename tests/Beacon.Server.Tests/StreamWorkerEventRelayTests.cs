using System.Threading.Channels;
using Beacon.Core.Diagnostics;
using Beacon.Core.Input;
using Beacon.Platform.Windows.Streaming;
using Beacon.Server.Streaming;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Beacon.Server.Tests;

public sealed class StreamWorkerEventRelayTests
{
    private static WorkerCapabilities AvailableCapabilities()
    {
        var capabilities = new WorkerCapabilities
        {
            WorkerInstanceId = Google.Protobuf.ByteString.CopyFrom(new byte[] { 1 }),
            QuicDatagrams = true,
            MaximumSessions = 1,
            MaximumFramesPerSecond = 120,
            VideoAvailable = true
        };
        capabilities.VideoCodecs.Add(WorkerVideoCodec.H264);
        capabilities.VideoEncoders.Add(WorkerVideoEncoder.Nvenc);
        capabilities.CaptureMethods.Add(WorkerCaptureMethod.WindowsGraphicsCapture);
        return capabilities;
    }

    [Fact]
    public async Task HostedStopDrainsBackpressuredWorkerEventsBeforeStoppingRelay()
    {
        var worker = new EventHost(capacity: 1) { PublishExitOnShutdown = true };
        var runtime = new RuntimeEvents();
        var sink = new BlockingSink();
        var journal = new InMemoryDiagnosticEventJournal();
        using IHost host = CreateHostedRelay(worker, runtime, sink, journal);
        await host.StartAsync(CancellationToken.None);
        await worker.WriteAsync(Input(1, ClientInputEvent.StreamKeyboard(1, true)));
        await sink.FirstEntered;
        await worker.WriteAsync(Input(2, ClientInputEvent.StreamKeyboard(2, false)));

        Task stop = host.StopAsync(CancellationToken.None);
        Task firstStopSignal = await Task.WhenAny(stop, worker.ShutdownEntered);

        Assert.Same(worker.ShutdownEntered, firstStopSignal);
        Assert.False(stop.IsCompleted);

        sink.ReleaseFirst();
        await sink.SecondEntered;
        Assert.False(stop.IsCompleted);
        sink.ReleaseSecond();
        await stop;

        Assert.Equal(1, worker.ShutdownCalls);
        Assert.Equal(2, sink.Calls);
        Assert.Equal(1, runtime.ExitCalls);
        Assert.Contains(journal.GetRecent(100), value => value.Operation == "worker.process_exited");
    }

    [Fact]
    public async Task HostedStopFailureStopsRelayWithSanitizedDiagnostic()
    {
        const string canary = "SHUTDOWN-CANARY-6f2d";
        var worker = new EventHost(capacity: 1)
        {
            ShutdownError = new InvalidOperationException(canary)
        };
        var runtime = new RuntimeEvents();
        var journal = new InMemoryDiagnosticEventJournal();
        using IHost host = CreateHostedRelay(worker, runtime, new RecordingSink(0), journal);
        await host.StartAsync(CancellationToken.None);

        await host.StopAsync(CancellationToken.None);

        DiagnosticEvent failure = Assert.Single(
            journal.GetRecent(100),
            value => value.Operation == "worker.shutdown_failed");
        string rendered = $"{failure.Operation}:{failure.Message}:{string.Join(',', failure.Metadata.Values)}";
        Assert.Equal("Worker shutdown failed.", failure.Message);
        Assert.DoesNotContain(canary, rendered, StringComparison.Ordinal);
        Assert.Equal(1, worker.ShutdownCalls);
    }

    [Fact]
    public async Task RelaysAllFourInputVariantsExactlyOnce()
    {
        var host = new EventHost();
        var runtime = new RuntimeEvents();
        var sink = new RecordingSink(expectedCalls: 4);
        var journal = new InMemoryDiagnosticEventJournal();
        var relay = new StreamWorkerEventRelay(host, runtime, sink, journal);
        await relay.StartAsync(CancellationToken.None);
        await host.WriteAsync(new StreamWorkerTransportAuthenticated(1, "session", 7, 1200));
        await host.WriteAsync(Input(1, ClientInputEvent.StreamPointer(
            ClientPointerAction.Scroll, -5, 6, -120, 2)));
        await host.WriteAsync(Input(2, ClientInputEvent.StreamKeyboard(0xE04D, false)));
        await host.WriteAsync(Input(3, ClientInputEvent.StreamController(2, 3, -4)));
        await host.WriteAsync(Input(4, ClientInputEvent.StreamTouch(
            5, ClientTouchAction.Move, 6, 7, 8, 9, 10)));

        await sink.Completed;
        await relay.StopAsync(CancellationToken.None);

        Assert.Equal(4, sink.Batches.Count);
        Assert.Equal([1, 2, 3, 4], sink.Batches.Select(batch => batch.Sequence));
        Assert.Equal(-120, sink.Batches[0].Events.Single().Pointer?.WheelDelta);
        Assert.Equal(0xE04Du, sink.Batches[1].Events.Single().Keyboard?.ScanCode);
        Assert.Equal(-4, sink.Batches[2].Events.Single().Controller?.Value);
        Assert.Equal(8u, sink.Batches[3].Events.Single().Touch?.CoordinateDenominator);
        Assert.Equal(1, runtime.BindCalls);
        Assert.Equal(4, runtime.ResolveCalls);
    }

    [Fact]
    public async Task SanitizesDiagnosticsAndContinuesAfterFailuresAndWorkerExit()
    {
        const string canary = "RELAY-CANARY-4d9c";
        var host = new EventHost();
        var runtime = new RuntimeEvents();
        var sink = new RecordingSink(expectedCalls: 3)
        {
            Result = call => call switch
            {
                1 => throw new InvalidOperationException(canary),
                2 => ClientInputResult.Fail(canary),
                _ => ClientInputResult.Ok(1),
            }
        };
        var journal = new InMemoryDiagnosticEventJournal();
        var relay = new StreamWorkerEventRelay(host, runtime, sink, journal);
        await relay.StartAsync(CancellationToken.None);
        await host.WriteAsync(new StreamWorkerTransportAuthenticated(1, "session", 7, 1200));
        await host.WriteAsync(Input(1, new ClientInputEvent("keyboard", "press", Key: canary, Code: canary)));
        await host.WriteAsync(new StreamWorkerFeedbackReceived(
            1, "session", 7, 2, StreamWorkerFeedbackKind.Decoder, 3, 4, 0));
        await host.WriteAsync(new StreamWorkerMediaEvidence(1, "session", 7, 5, 6, 7));
        await host.WriteAsync(new StreamWorkerConnectionObserved(1, 22));
        await host.WriteAsync(new StreamWorkerConnectionConfigured(1, 22));
        await host.WriteAsync(new StreamWorkerTransportConnected(1, 22));
        await host.WriteAsync(new StreamWorkerTransportFailed(1, 22, 0x80410006));
        await host.WriteAsync(new StreamWorkerSessionStateChanged(
            1, "session", WorkerSessionState.Failed, WorkerErrorCode.OperationFailed));
        await host.WriteAsync(new StreamWorkerSessionFailure(
            1,
            "session",
            7,
            DiagnosticBoundary.Capture,
            DiagnosticCode.OperationFailed,
            2,
            "capture-session-create"));
        await host.WriteAsync(new StreamWorkerProcessExited(1, 23));
        await host.WriteAsync(Input(8, ClientInputEvent.StreamKeyboard(1, true)));
        await host.WriteAsync(Input(9, ClientInputEvent.StreamKeyboard(2, false)));

        await sink.Completed;
        await relay.StopAsync(CancellationToken.None);

        string rendered = string.Join('|', journal.GetRecent(100).Select(value =>
            $"{value.Operation}:{value.Message}:{string.Join(',', value.Metadata.Select(pair => $"{pair.Key}={pair.Value}"))}"));
        Assert.DoesNotContain(canary, rendered, StringComparison.Ordinal);
        Assert.Contains("worker.process_exited", rendered, StringComparison.Ordinal);
        Assert.Contains("worker.feedback", rendered, StringComparison.Ordinal);
        Assert.Contains("worker.media", rendered, StringComparison.Ordinal);
        Assert.Contains("worker.connection_observed", rendered, StringComparison.Ordinal);
        Assert.Contains("worker.connection_configured", rendered, StringComparison.Ordinal);
        Assert.Contains("worker.transport_connected", rendered, StringComparison.Ordinal);
        Assert.Contains("worker.transport_failed", rendered, StringComparison.Ordinal);
        Assert.Contains("worker.session_failed", rendered, StringComparison.Ordinal);
        Assert.Contains("worker.video_failed", rendered, StringComparison.Ordinal);
        Assert.Contains("boundary=Capture", rendered, StringComparison.Ordinal);
        Assert.Contains("failureStage=capture-session-create", rendered, StringComparison.Ordinal);
        Assert.Contains("platformStatusCode=2151743494", rendered, StringComparison.Ordinal);
        Assert.Contains("input.dispatch_failed", rendered, StringComparison.Ordinal);
        Assert.Contains("input.rejected", rendered, StringComparison.Ordinal);
        Assert.Equal(1, runtime.ExitCalls);
        Assert.Equal(3, sink.Batches.Count);
    }

    private static StreamWorkerInputReceived Input(long sequence, ClientInputEvent input) =>
        new(1, "session", 7, checked((ulong)sequence), [input]);

    private static IHost CreateHostedRelay(
        EventHost worker,
        RuntimeEvents runtime,
        IClientInputSink sink,
        IDiagnosticEventSink diagnostics) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IStreamWorkerHost>(worker);
                services.AddSingleton<IStreamWorkerRuntimeEvents>(runtime);
                services.AddSingleton(sink);
                services.AddSingleton(diagnostics);
                services.AddHostedService<StreamWorkerEventRelay>();
            })
            .Build();

    private sealed class EventHost : IStreamWorkerHost
    {
        private readonly Channel<StreamWorkerEvent> channel;
        private readonly TaskCompletionSource shutdownEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public EventHost(int capacity = 64)
        {
            channel = Channel.CreateBounded<StreamWorkerEvent>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        }

        public bool IsReady { get; private set; } = true;
        public ReadOnlyMemory<byte> WorkerInstanceId => new byte[] { 1 };

        public WorkerCapabilities Capabilities { get; } = AvailableCapabilities();
        public ChannelReader<StreamWorkerEvent> Events => channel.Reader;
        public long CurrentProcessGeneration => 1;
        public Task ShutdownEntered => shutdownEntered.Task;
        public int ShutdownCalls { get; private set; }
        public bool PublishExitOnShutdown { get; init; }
        public Exception? ShutdownError { get; init; }
        public bool IsCurrentProcessGeneration(long processGeneration) => processGeneration == 1;
        public ValueTask WriteAsync(StreamWorkerEvent value) => channel.Writer.WriteAsync(value);
        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<StreamWorkerCommandResponse> SendAsync(
            long expectedProcessGeneration,
            WorkerIpcEnvelope command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownCalls++;
            shutdownEntered.TrySetResult();
            IsReady = false;
            if (ShutdownError is not null)
            {
                throw ShutdownError;
            }
            if (PublishExitOnShutdown)
            {
                await channel.Writer.WriteAsync(new StreamWorkerProcessExited(1, 0), cancellationToken);
            }
        }
    }

    private sealed class RuntimeEvents : IStreamWorkerRuntimeEvents
    {
        public int BindCalls { get; private set; }
        public int ResolveCalls { get; private set; }
        public int ExitCalls { get; private set; }

        public bool TryBind(StreamWorkerTransportAuthenticated authenticated)
        {
            BindCalls++;
            return true;
        }

        public bool TryResolveInput(StreamWorkerInputReceived input, out ClientInputBatch? batch)
        {
            ResolveCalls++;
            batch = new ClientInputBatch("client", input.SessionId!, "display", checked((long)input.Sequence), input.Events);
            return true;
        }

        public bool IsCurrent(StreamWorkerFeedbackReceived feedback) => true;
        public bool IsCurrent(StreamWorkerMediaEvidence media) => true;
        public bool TryDisconnect(StreamWorkerTransportDisconnected disconnected) => true;
        public void ProcessExited(StreamWorkerProcessExited exited) => ExitCalls++;
        public Guid? GetBoundRuntimeGeneration(string sessionId) => Guid.NewGuid();
    }

    private sealed class RecordingSink(int expectedCalls) : IClientInputSink
    {
        private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<ClientInputBatch> Batches { get; } = [];
        public Func<int, ClientInputResult> Result { get; set; } = _ => ClientInputResult.Ok(1);
        public Task Completed => completed.Task;

        public Task<ClientInputResult> ForwardAsync(ClientInputBatch batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            int call = Batches.Count;
            if (call == expectedCalls)
            {
                completed.TrySetResult();
            }
            return Task.FromResult(Result(call));
        }
    }

    private sealed class BlockingSink : IClientInputSink
    {
        private readonly TaskCompletionSource firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource secondEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseSecond =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }
        public Task FirstEntered => firstEntered.Task;
        public Task SecondEntered => secondEntered.Task;

        public void ReleaseFirst() => releaseFirst.TrySetResult();
        public void ReleaseSecond() => releaseSecond.TrySetResult();

        public async Task<ClientInputResult> ForwardAsync(
            ClientInputBatch batch,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }
            else if (Calls == 2)
            {
                secondEntered.TrySetResult();
                await releaseSecond.Task.WaitAsync(cancellationToken);
            }
            return ClientInputResult.Ok(1);
        }
    }
}
