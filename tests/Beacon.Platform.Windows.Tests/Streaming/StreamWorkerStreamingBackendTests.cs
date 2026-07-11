using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Streaming;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class StreamWorkerStreamingBackendTests
{
    [Fact]
    public async Task StartMapsPlanAndStopKeepsWorkerReady()
    {
        var host = new RecordingStreamWorkerHost();
        var backend = new StreamWorkerStreamingBackend(host);
        SessionPlan plan = CreatePlan();

        StreamingPreflightResult preflight = await backend.CheckReadinessAsync(plan, CancellationToken.None);
        StreamingStartResult start = await backend.StartAsync(plan, CancellationToken.None);
        StreamingStopResult stop = await backend.StopAsync(plan.SessionId, CancellationToken.None);

        Assert.True(preflight.Success);
        Assert.True(start.Success);
        Assert.Equal("running", start.Session?.State);
        Assert.True(stop.Success);
        Assert.Equal("stopped", stop.Session?.State);
        Assert.Equal(3, host.Commands.Count);
        PrepareSession prepare = Assert.IsType<PrepareSession>(host.Commands[0].PrepareSession);
        Assert.Equal("Z Fold 7", host.Commands[0].SessionId);
        Assert.Equal(2560u, prepare.Width);
        Assert.Equal(1600u, prepare.Height);
        Assert.Equal(120u, prepare.FramesPerSecondNumerator);
        Assert.Equal(WorkerVideoCodec.H264, prepare.VideoCodec);
        Assert.Equal(WorkerDynamicRange.Sdr, prepare.DynamicRange);
        Assert.Equal(45000u, prepare.InitialBitrateKbps);
        Assert.Equal(WorkerIpcEnvelope.BodyOneofCase.StartMedia, host.Commands[1].BodyCase);
        Assert.Equal(WorkerIpcEnvelope.BodyOneofCase.StopMedia, host.Commands[2].BodyCase);
        Assert.True(host.IsReady);
        Assert.Equal(0, host.ShutdownCalls);
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
        new PlannedStream("h264", 120, 45, "beacon-quic", "adaptive", "test"));

    private sealed class RecordingStreamWorkerHost : IStreamWorkerHost
    {
        public bool IsReady { get; private set; } = true;

        public WorkerErrorCode NextError { get; set; } = WorkerErrorCode.None;

        public int ShutdownCalls { get; private set; }

        public Exception? ReadinessError { get; set; }

        public List<WorkerIpcEnvelope> Commands { get; } = [];

        public Task EnsureReadyAsync(CancellationToken cancellationToken)
        {
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
            var completion = new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                SessionId = command.SessionId,
                WorkerCompletion = new WorkerCompletion
                {
                    Succeeded = NextError == WorkerErrorCode.None,
                    ErrorCode = NextError,
                },
            };
            NextError = WorkerErrorCode.None;
            return Task.FromResult(new StreamWorkerCommandResponse(completion, []));
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownCalls++;
            IsReady = false;
            return Task.CompletedTask;
        }
    }
}
