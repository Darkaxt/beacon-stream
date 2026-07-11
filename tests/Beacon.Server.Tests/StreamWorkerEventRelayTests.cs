using System.Threading.Channels;
using Beacon.Core.Diagnostics;
using Beacon.Core.Input;
using Beacon.Platform.Windows.Streaming;
using Beacon.Server.Streaming;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Server.Tests;

public sealed class StreamWorkerEventRelayTests
{
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
        Assert.Contains("input.dispatch_failed", rendered, StringComparison.Ordinal);
        Assert.Contains("input.rejected", rendered, StringComparison.Ordinal);
        Assert.Equal(1, runtime.ExitCalls);
        Assert.Equal(3, sink.Batches.Count);
    }

    private static StreamWorkerInputReceived Input(long sequence, ClientInputEvent input) =>
        new(1, "session", 7, checked((ulong)sequence), [input]);

    private sealed class EventHost : IStreamWorkerHost
    {
        private readonly Channel<StreamWorkerEvent> channel = Channel.CreateUnbounded<StreamWorkerEvent>();

        public bool IsReady => true;
        public ReadOnlyMemory<byte> WorkerInstanceId => new byte[] { 1 };
        public ChannelReader<StreamWorkerEvent> Events => channel.Reader;
        public long CurrentProcessGeneration => 1;
        public bool IsCurrentProcessGeneration(long processGeneration) => processGeneration == 1;
        public ValueTask WriteAsync(StreamWorkerEvent value) => channel.Writer.WriteAsync(value);
        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<StreamWorkerCommandResponse> SendAsync(
            WorkerIpcEnvelope command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
}
