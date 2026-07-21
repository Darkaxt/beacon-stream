using System.Buffers.Binary;
using System.IO.Pipes;
using System.Threading.Channels;
using Beacon.Core.Input;
using Beacon.Platform.Windows.Streaming;
using Beacon.StreamWorker.Contracts.Framing;
using StreamContracts = Beacon.StreamWorker.Contracts.Stream.V1;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Google.Protobuf;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class StreamWorkerNamedPipeClientTests
{
    [Fact]
    public async Task IdentityBoundCapabilitiesAreRequiredBeforeReady()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
        });
        await using var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, 42);

        await client.InitializeAsync(CancellationToken.None);

        Assert.True(client.IsReady);
        Assert.True(client.Capabilities.VideoAvailable);
        Assert.Equal(WorkerVideoEncoder.Nvenc, Assert.Single(client.Capabilities.VideoEncoders));
        await worker;
    }

    [Fact]
    public async Task ForeignCapabilityIdentityFailsTheHandshake()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            WorkerIpcEnvelope capabilities = Capabilities(1);
            capabilities.WorkerCapabilities.WorkerInstanceId = ByteString.CopyFrom(new byte[] { 9, 9, 9 });
            await WriteAsync(pipes.Worker, capabilities);
        });
        await using var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, 42);

        StreamWorkerProtocolException error = await Assert.ThrowsAsync<StreamWorkerProtocolException>(
            () => client.InitializeAsync(CancellationToken.None));

        Assert.Equal("StreamWorker capabilities are invalid.", error.Message);
        Assert.False(client.IsReady);
        await worker;
    }

    [Fact]
    public async Task UnavailableVideoRequiresAndPreservesTypedDiagnostic()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(
                pipes.Worker,
                Capabilities(
                    1,
                    videoAvailable: false,
                    unavailableBoundary: DiagnosticBoundary.Encoder,
                    unavailableCode: 7));
            await WriteAsync(pipes.Worker, Ready(1));
        });
        await using var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, 42);

        await client.InitializeAsync(CancellationToken.None);

        Assert.False(client.Capabilities.VideoAvailable);
        Assert.Equal(DiagnosticBoundary.Encoder, client.Capabilities.VideoUnavailableBoundary);
        Assert.Equal(7u, client.Capabilities.VideoUnavailableCode);
        await worker;
    }

    [Fact]
    public async Task UnavailableVideoWithoutTypedDiagnosticFailsTheHandshake()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1, videoAvailable: false));
        });
        await using var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, 42);

        await Assert.ThrowsAsync<StreamWorkerProtocolException>(
            () => client.InitializeAsync(CancellationToken.None));

        Assert.False(client.IsReady);
        await worker;
    }

    [Fact]
    public async Task ZeroIdConnectionObservedDiagnosticUsesNeutralMetadataEvent()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = Channel.CreateBounded<StreamWorkerEvent>(1);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            await WriteAsync(pipes.Worker, new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                WorkerDiagnostic = new WorkerDiagnostic
                {
                    Severity = DiagnosticSeverity.Information,
                    Boundary = DiagnosticBoundary.Transport,
                    Code = DiagnosticCode.ConnectionObserved,
                    NumericValue = 17
                }
            });
        });
        await using var client = new StreamWorkerNamedPipeClient(
            pipes.Service, processExit.Task, 42, processGeneration: 9, events.Writer);
        await client.InitializeAsync(CancellationToken.None);

        StreamWorkerConnectionObserved observed = Assert.IsType<StreamWorkerConnectionObserved>(
            await events.Reader.ReadAsync());

        Assert.Equal(9, observed.ProcessGeneration);
        Assert.Null(observed.SessionId);
        Assert.Equal(17UL, observed.ConnectionGeneration);
        Assert.DoesNotContain(
            observed.GetType().GetProperties(),
            property => typeof(IMessage).IsAssignableFrom(property.PropertyType));
        await worker;
    }

    [Fact]
    public async Task ZeroIdConnectionMilestonesUseNeutralMetadataEvents()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = Channel.CreateBounded<StreamWorkerEvent>(3);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            await WriteAsync(pipes.Worker, ConnectionDiagnostic(DiagnosticCode.ConnectionConfigured, 17));
            await WriteAsync(pipes.Worker, ConnectionDiagnostic(DiagnosticCode.TransportConnected, 17));
            await WriteAsync(pipes.Worker, ConnectionDiagnostic(
                DiagnosticCode.TransportFailed,
                17,
                platformErrorCode: 0x80410006));
        });
        await using var client = new StreamWorkerNamedPipeClient(
            pipes.Service, processExit.Task, 42, processGeneration: 9, events.Writer);
        await client.InitializeAsync(CancellationToken.None);

        StreamWorkerConnectionConfigured configured = Assert.IsType<StreamWorkerConnectionConfigured>(
            await events.Reader.ReadAsync());
        StreamWorkerTransportConnected connected = Assert.IsType<StreamWorkerTransportConnected>(
            await events.Reader.ReadAsync());
        StreamWorkerTransportFailed failed = Assert.IsType<StreamWorkerTransportFailed>(
            await events.Reader.ReadAsync());

        Assert.Equal(17UL, configured.ConnectionGeneration);
        Assert.Equal(17UL, connected.ConnectionGeneration);
        Assert.Equal(17UL, failed.ConnectionGeneration);
        Assert.Equal(0x80410006u, failed.PlatformStatusCode);
        Assert.All(
            new StreamWorkerEvent[] { configured, connected, failed },
            value =>
            {
                Assert.Equal(9, value.ProcessGeneration);
                Assert.Null(value.SessionId);
                Assert.DoesNotContain(
                    value.GetType().GetProperties(),
                    property => typeof(IMessage).IsAssignableFrom(property.PropertyType));
            });
        await worker;
    }

    [Fact]
    public async Task SessionVideoFailureEventsRemainTypedWithoutBreakingTheControlPipe()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = Channel.CreateBounded<StreamWorkerEvent>(2);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            await WriteAsync(pipes.Worker, new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                SessionId = "session-a",
                SessionStateChanged = new SessionStateChanged
                {
                    State = WorkerSessionState.Failed,
                    ErrorCode = WorkerErrorCode.OperationFailed
                }
            });
            await WriteAsync(pipes.Worker, new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                SessionId = "session-a",
                WorkerDiagnostic = new WorkerDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Boundary = DiagnosticBoundary.Capture,
                    Code = DiagnosticCode.OperationFailed,
                    PlatformErrorCode = 2,
                    NumericValue = 17,
                    FailureStage = "capture-session-create"
                }
            });
        });
        await using var client = new StreamWorkerNamedPipeClient(
            pipes.Service, processExit.Task, 42, processGeneration: 9, events.Writer);
        await client.InitializeAsync(CancellationToken.None);

        Task<StreamWorkerEvent> stateRead = events.Reader.ReadAsync().AsTask();
        Assert.Same(stateRead, await Task.WhenAny(stateRead, client.Completion));
        StreamWorkerSessionStateChanged state = Assert.IsType<StreamWorkerSessionStateChanged>(
            await stateRead);
        Task<StreamWorkerEvent> failureRead = events.Reader.ReadAsync().AsTask();
        Assert.Same(failureRead, await Task.WhenAny(failureRead, client.Completion));
        StreamWorkerSessionFailure failure = Assert.IsType<StreamWorkerSessionFailure>(
            await failureRead);

        Assert.Equal(9, state.ProcessGeneration);
        Assert.Equal("session-a", state.SessionId);
        Assert.Equal(WorkerSessionState.Failed, state.State);
        Assert.Equal(WorkerErrorCode.OperationFailed, state.ErrorCode);
        Assert.Equal(17UL, failure.WorkerSessionGeneration);
        Assert.Equal(DiagnosticBoundary.Capture, failure.Boundary);
        Assert.Equal(2u, failure.PlatformErrorCode);
        Assert.Equal("capture-session-create", failure.FailureStage);
        Assert.True(client.IsReady);
        Assert.Null(client.TerminalError);
        await worker;
    }

    [Fact]
    public async Task LegacyConstructorFailsClosedOnFirstUnsolicitedEventWithoutBlockingCommand()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Task> worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            _ = await ReadAsync(pipes.Worker);
            await WriteAsync(pipes.Worker, InputEventEnvelope());
            return WriteAsync(pipes.Worker, InputEventEnvelope());
        });
        var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, 42);
        await client.InitializeAsync(CancellationToken.None);

        Task<StreamWorkerCommandResponse> pending = client.SendAsync(
            Command("session-a"),
            CancellationToken.None);
        Task secondWrite = await worker;
        await client.Completion;

        await Assert.ThrowsAsync<StreamWorkerProtocolException>(() => pending);
        Assert.False(client.IsReady);
        Assert.IsType<StreamWorkerProtocolException>(client.TerminalError);
        await client.DisposeAsync();
        try
        {
            await secondWrite;
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException)
        {
        }
    }

    [Fact]
    public async Task ZeroIdTransportFeedbackMediaAndDisconnectUseNeutralScalarEvents()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = Channel.CreateBounded<StreamWorkerEvent>(4);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            await WriteAsync(pipes.Worker, new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                SessionId = "session-a",
                TransportAuthenticated = new TransportAuthenticated
                {
                    SessionGeneration = 17,
                    MaximumDatagramBytes = 1200
                }
            });
            var feedback = new StreamContracts.FeedbackStreamEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                SessionId = "session-a",
                Sequence = 18,
                QueueDepth = new StreamContracts.QueueDepthFeedback
                {
                    QueuedAccessUnits = 3,
                    DroppedAccessUnits = 4
                }
            };
            await WriteAsync(pipes.Worker, new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                SessionId = "session-a",
                FeedbackReceived = new FeedbackReceived { SessionGeneration = 17, Feedback = feedback }
            });
            await WriteAsync(pipes.Worker, new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                SessionId = "session-a",
                MediaEvidence = new MediaEvidence
                {
                    SessionGeneration = 17,
                    Sequence = 19,
                    PresentationTimeUs = 20,
                    DatagramBytes = 21
                }
            });
            await WriteAsync(pipes.Worker, new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                SessionId = "session-a",
                TransportDisconnected = new TransportDisconnected { SessionGeneration = 17 }
            });
        });
        await using var client = new StreamWorkerNamedPipeClient(
            pipes.Service, processExit.Task, 42, processGeneration: 9, events.Writer);
        await client.InitializeAsync(CancellationToken.None);

        StreamWorkerEvent[] translated =
        [
            await events.Reader.ReadAsync(),
            await events.Reader.ReadAsync(),
            await events.Reader.ReadAsync(),
            await events.Reader.ReadAsync()
        ];

        StreamWorkerTransportAuthenticated authenticated = Assert.IsType<StreamWorkerTransportAuthenticated>(translated[0]);
        Assert.Equal(1200u, authenticated.MaximumDatagramBytes);
        StreamWorkerFeedbackReceived received = Assert.IsType<StreamWorkerFeedbackReceived>(translated[1]);
        Assert.Equal(StreamWorkerFeedbackKind.QueueDepth, received.Kind);
        Assert.Equal(3UL, received.PrimaryValue);
        Assert.Equal(4UL, received.SecondaryValue);
        StreamWorkerMediaEvidence media = Assert.IsType<StreamWorkerMediaEvidence>(translated[2]);
        Assert.Equal(21u, media.DatagramBytes);
        Assert.IsType<StreamWorkerTransportDisconnected>(translated[3]);
        Assert.All(translated, value => Assert.DoesNotContain(
            value.GetType().GetProperties(),
            property => typeof(IMessage).IsAssignableFrom(property.PropertyType)));
        await worker;
    }

    [Fact]
    public async Task BenchmarkDatagramEchoIsTranslatedAsBenchmarkFeedback()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = Channel.CreateBounded<StreamWorkerEvent>(1);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            await WriteAsync(pipes.Worker, new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                SessionId = "benchmark:run-a",
                FeedbackReceived = new FeedbackReceived
                {
                    SessionGeneration = 17,
                    Feedback = new StreamContracts.FeedbackStreamEnvelope
                    {
                        ProtocolVersion = ProtocolVersion.Current,
                        SessionId = "benchmark:run-a",
                        Sequence = 18,
                        BenchmarkDatagramEcho = new StreamContracts.BenchmarkDatagramEcho
                        {
                            RunId = "run-a",
                            RoundId = 2,
                            Sequence = 7
                        }
                    }
                }
            });
        });
        await using var client = new StreamWorkerNamedPipeClient(
            pipes.Service, processExit.Task, 42, processGeneration: 9, events.Writer);
        await client.InitializeAsync(CancellationToken.None);

        StreamWorkerFeedbackReceived received = Assert.IsType<StreamWorkerFeedbackReceived>(
            await events.Reader.ReadAsync());

        Assert.Equal(StreamWorkerFeedbackKind.BenchmarkDatagramEcho, received.Kind);
        Assert.Equal(2UL, received.PrimaryValue);
        Assert.Equal(7UL, received.SecondaryValue);
        Assert.Equal(0u, received.Count);
        Assert.True(client.IsReady);
        Assert.Null(client.TerminalError);
        await worker;
    }

    [Fact]
    public async Task ZeroIdInputIsTranslatedExactlyWithoutProtobufEscaping()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = Channel.CreateBounded<StreamWorkerEvent>(1);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            await WriteAsync(pipes.Worker, InputEventEnvelope());
        });
        await using var client = new StreamWorkerNamedPipeClient(
            pipes.Service, processExit.Task, 42, processGeneration: 9, events.Writer);
        await client.InitializeAsync(CancellationToken.None);

        StreamWorkerInputReceived received = Assert.IsType<StreamWorkerInputReceived>(
            await events.Reader.ReadAsync());

        Assert.Equal(9, received.ProcessGeneration);
        Assert.Equal("session-a", received.SessionId);
        Assert.Equal(17UL, received.WorkerSessionGeneration);
        Assert.Equal(81UL, received.Sequence);
        Assert.Collection(
            received.Events,
            value => Assert.Equal(
                new ClientPointerInput(ClientPointerAction.Scroll, -10, 20, -120, 2),
                value.Pointer),
            value => Assert.Equal(new ClientKeyboardInput(0x1E, true), value.Keyboard),
            value => Assert.Equal(new ClientControllerInput(2, 7, -123), value.Controller),
            value => Assert.Equal(
                new ClientTouchInput(4, ClientTouchAction.Move, 1, 2, 3, 4, 5),
                value.Touch));
        Assert.DoesNotContain(
            typeof(WorkerIpcEnvelope),
            received.GetType().GetProperties().Select(property => property.PropertyType));
        await worker;
    }

    [Fact]
    public async Task PositiveRequestEventIsCorrelatedOnlyAndNeverPublished()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = Channel.CreateBounded<StreamWorkerEvent>(1);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            WorkerIpcEnvelope request = await ReadAsync(pipes.Worker);
            WorkerIpcEnvelope correlated = InputEventEnvelope();
            correlated.RequestId = request.RequestId;
            await WriteAsync(pipes.Worker, correlated);
            await WriteAsync(pipes.Worker, Completion(request, succeeded: true));
        });
        await using var client = new StreamWorkerNamedPipeClient(
            pipes.Service, processExit.Task, 42, processGeneration: 9, events.Writer);
        await client.InitializeAsync(CancellationToken.None);

        StreamWorkerCommandResponse response = await client.SendAsync(Command("session-a"), CancellationToken.None);

        Assert.Single(response.Events);
        Assert.False(events.Reader.TryRead(out _));
        await worker;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MalformedOrUnknownZeroIdEventFailsGeneration(bool malformed)
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = Channel.CreateBounded<StreamWorkerEvent>(1);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            WorkerIpcEnvelope invalid;
            if (malformed)
            {
                invalid = InputEventEnvelope();
                invalid.SessionId = string.Empty;
            }
            else
            {
                invalid = new WorkerIpcEnvelope
                {
                    ProtocolVersion = ProtocolVersion.Current,
                    WorkerHealth = new WorkerHealth { LifecycleState = WorkerLifecycleState.Ready }
                };
            }
            await WriteAsync(pipes.Worker, invalid);
        });
        await using var client = new StreamWorkerNamedPipeClient(
            pipes.Service, processExit.Task, 42, processGeneration: 9, events.Writer);
        await client.InitializeAsync(CancellationToken.None);

        await client.Completion;

        Assert.False(client.IsReady);
        Assert.IsType<StreamWorkerProtocolException>(client.TerminalError);
        await worker;
    }

    [Fact]
    public async Task DisposalCancelsWriterBlockedByBoundedEventBackpressure()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = Channel.CreateBounded<StreamWorkerEvent>(1);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            await WriteAsync(pipes.Worker, InputEventEnvelope());
            await WriteAsync(pipes.Worker, InputEventEnvelope());
        });
        var client = new StreamWorkerNamedPipeClient(
            pipes.Service, processExit.Task, 42, processGeneration: 9, events.Writer);
        await client.InitializeAsync(CancellationToken.None);
        await worker;

        await client.DisposeAsync();

        Assert.True(client.Completion.IsCompletedSuccessfully);
    }
    [Fact]
    public async Task InitializeRequiresMatchingHelloAndReadyMessages()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(processId: 42, version: 1));
            await WriteAsync(pipes.Worker, Capabilities(version: 1));
            await WriteAsync(pipes.Worker, Ready(version: 1));
        });
        await using var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, expectedProcessId: 42);

        await client.InitializeAsync(CancellationToken.None);

        Assert.True(client.IsReady);
        await worker;
    }

    [Fact]
    public async Task InitializeRejectsProtocolAndProcessIdentityMismatch()
    {
        await using PipePair versionPipes = await PipePair.CreateAsync();
        var versionExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task versionWorker = Task.Run(async () =>
        {
            await WriteAsync(versionPipes.Worker, Hello(processId: 42, version: 2));
        });
        await using var versionClient = new StreamWorkerNamedPipeClient(
            versionPipes.Service,
            versionExit.Task,
            expectedProcessId: 42);

        await Assert.ThrowsAsync<UnsupportedProtocolVersionException>(
            () => versionClient.InitializeAsync(CancellationToken.None));
        await versionWorker;

        await using PipePair processPipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task processWorker = Task.Run(async () =>
        {
            await WriteAsync(processPipes.Worker, Hello(processId: 41, version: 1));
        });
        await using var processClient = new StreamWorkerNamedPipeClient(
            processPipes.Service,
            processExit.Task,
            expectedProcessId: 42);

        await Assert.ThrowsAsync<StreamWorkerProtocolException>(
            () => processClient.InitializeAsync(CancellationToken.None));
        await processWorker;
    }

    [Fact]
    public async Task ConcurrentRequestsCompleteByRequestIdInsteadOfArrivalOrder()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            WorkerIpcEnvelope first = await ReadAsync(pipes.Worker);
            WorkerIpcEnvelope second = await ReadAsync(pipes.Worker);
            await WriteAsync(pipes.Worker, Completion(second, succeeded: true));
            await WriteAsync(pipes.Worker, Completion(first, succeeded: true));
        });
        await using var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, 42);
        await client.InitializeAsync(CancellationToken.None);

        Task<StreamWorkerCommandResponse> firstTask = client.SendAsync(
            Command("session-a"),
            CancellationToken.None);
        Task<StreamWorkerCommandResponse> secondTask = client.SendAsync(
            Command("session-b"),
            CancellationToken.None);
        StreamWorkerCommandResponse[] responses = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal("session-a", responses[0].Completion.SessionId);
        Assert.Equal("session-b", responses[1].Completion.SessionId);
        Assert.NotEqual(responses[0].Completion.RequestId, responses[1].Completion.RequestId);
        await worker;
    }

    [Fact]
    public async Task CompletionWithMismatchedSessionIdFailsPendingRequest()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            WorkerIpcEnvelope request = await ReadAsync(pipes.Worker);
            WorkerIpcEnvelope completion = Completion(request, succeeded: true);
            completion.SessionId = "session-b";
            await WriteAsync(pipes.Worker, completion);
        });
        await using var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, 42);
        await client.InitializeAsync(CancellationToken.None);

        StreamWorkerProtocolException error = await Assert.ThrowsAsync<StreamWorkerProtocolException>(
            () => client.SendAsync(Command("session-a"), CancellationToken.None));

        Assert.Equal("StreamWorker response session identity is invalid.", error.Message);
        Assert.IsType<StreamWorkerProtocolException>(client.TerminalError);
        await worker;
    }

    [Fact]
    public async Task OversizedFrameFailsPendingRequestBeforePayloadAllocation()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            _ = await ReadAsync(pipes.Worker);
            byte[] oversized = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(
                oversized,
                ProtobufLengthFrameCodec.MaximumMessageBytes + 1);
            await pipes.Worker.WriteAsync(oversized);
            await pipes.Worker.FlushAsync();
        });
        await using var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, 42);
        await client.InitializeAsync(CancellationToken.None);

        Task<StreamWorkerCommandResponse> pending = client.SendAsync(
            Command("session-a"),
            CancellationToken.None);

        await Assert.ThrowsAsync<ProtobufFrameException>(() => pending);
        await worker;
    }

    [Fact]
    public async Task ProcessExitFailsPendingRequestsWithoutRenderingSecrets()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            _ = await ReadAsync(pipes.Worker);
            processExit.SetResult(23);
        });
        await using var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, 42);
        await client.InitializeAsync(CancellationToken.None);
        const string secret = "raw-secret-ticket";
        var request = Command("session-a");
        request.AuthorizeTicket = new AuthorizeTicket
        {
            TicketHash = ByteString.CopyFromUtf8(secret),
        };

        StreamWorkerProcessExitedException error = await Assert.ThrowsAsync<StreamWorkerProcessExitedException>(
            () => client.SendAsync(request, CancellationToken.None));

        Assert.Equal(23, error.ExitCode);
        Assert.DoesNotContain(secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(secret)), error.Message);
        await worker;
    }

    [Fact]
    public async Task ProtocolFaultClearsReadinessAndCompletesLifecycleSignal()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
            await WriteAsync(pipes.Worker, Capabilities(1));
            await WriteAsync(pipes.Worker, Ready(1));
            byte[] oversized = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(
                oversized,
                ProtobufLengthFrameCodec.MaximumMessageBytes + 1);
            await pipes.Worker.WriteAsync(oversized);
            await pipes.Worker.FlushAsync();
        });
        await using var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, 42);
        await client.InitializeAsync(CancellationToken.None);

        await client.Completion;

        Assert.False(client.IsReady);
        Assert.IsType<ProtobufFrameException>(client.TerminalError);
        await worker;
    }

    [Fact]
    public async Task ProcessExitDuringHandshakeCancelsOutstandingRead()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new StreamWorkerNamedPipeClient(pipes.Service, processExit.Task, 42);
        Task initialize = client.InitializeAsync(CancellationToken.None);

        processExit.SetResult(17);

        StreamWorkerProcessExitedException error =
            await Assert.ThrowsAsync<StreamWorkerProcessExitedException>(() => initialize);
        Assert.Equal(17, error.ExitCode);
    }

    private static WorkerIpcEnvelope Hello(uint processId, uint version)
    {
        var envelope = new WorkerIpcEnvelope { ProtocolVersion = version };
        envelope.WorkerHello = new WorkerHello
        {
            ProcessId = processId,
            WorkerInstanceId = ByteString.CopyFrom(new byte[] { 1, 2, 3 }),
        };
        return envelope;
    }

    private static WorkerIpcEnvelope Ready(uint version)
    {
        var envelope = new WorkerIpcEnvelope { ProtocolVersion = version };
        envelope.WorkerReady = new WorkerReady
        {
            WorkerInstanceId = ByteString.CopyFrom(new byte[] { 1, 2, 3 }),
        };
        return envelope;
    }

    private static WorkerIpcEnvelope Capabilities(
        uint version,
        bool videoAvailable = true,
        DiagnosticBoundary unavailableBoundary = DiagnosticBoundary.Unspecified,
        uint unavailableCode = 0)
    {
        var envelope = new WorkerIpcEnvelope
        {
            ProtocolVersion = version,
            WorkerCapabilities = new WorkerCapabilities
            {
                WorkerInstanceId = ByteString.CopyFrom(new byte[] { 1, 2, 3 }),
                QuicDatagrams = true,
                MaximumSessions = 1,
                MaximumFramesPerSecond = 120,
                VideoAvailable = videoAvailable,
                VideoUnavailableBoundary = unavailableBoundary,
                VideoUnavailableCode = unavailableCode
            }
        };
        envelope.WorkerCapabilities.VideoCodecs.Add(WorkerVideoCodec.H264);
        envelope.WorkerCapabilities.VideoEncoders.Add(WorkerVideoEncoder.Nvenc);
        envelope.WorkerCapabilities.CaptureMethods.Add(
            WorkerCaptureMethod.WindowsGraphicsCapture);
        return envelope;
    }

    private static WorkerIpcEnvelope Command(string sessionId) => new()
    {
        ProtocolVersion = ProtocolVersion.Current,
        SessionId = sessionId,
        RequestId = 0,
        RequestIdr = new RequestIdr { Reason = IdrReason.ClientRecovery },
    };

    private static WorkerIpcEnvelope Completion(WorkerIpcEnvelope request, bool succeeded) => new()
    {
        ProtocolVersion = ProtocolVersion.Current,
        RequestId = request.RequestId,
        SessionId = request.SessionId,
        WorkerCompletion = new WorkerCompletion
        {
            Succeeded = succeeded,
            ErrorCode = succeeded ? WorkerErrorCode.None : WorkerErrorCode.OperationFailed,
        },
    };

    private static WorkerIpcEnvelope ConnectionDiagnostic(
        DiagnosticCode code,
        ulong connectionGeneration,
        uint platformErrorCode = 0) => new()
        {
            ProtocolVersion = ProtocolVersion.Current,
            WorkerDiagnostic = new WorkerDiagnostic
            {
                Severity = DiagnosticSeverity.Information,
                Boundary = DiagnosticBoundary.Transport,
                Code = code,
                PlatformErrorCode = platformErrorCode,
                NumericValue = connectionGeneration
            }
        };

    private static WorkerIpcEnvelope InputEventEnvelope()
    {
        var input = new StreamContracts.InputStreamEnvelope
        {
            ProtocolVersion = ProtocolVersion.Current,
            SessionId = "session-a",
            Sequence = 81,
            InputBatch = new StreamContracts.InputBatch()
        };
        input.InputBatch.Events.Add(new StreamContracts.InputEvent
        {
            Pointer = new StreamContracts.PointerInput
            {
                Action = StreamContracts.PointerAction.Scroll,
                X = -10,
                Y = 20,
                WheelDelta = -120,
                Button = 2
            }
        });
        input.InputBatch.Events.Add(new StreamContracts.InputEvent
        {
            Keyboard = new StreamContracts.KeyboardInput { ScanCode = 0x1E, Pressed = true }
        });
        input.InputBatch.Events.Add(new StreamContracts.InputEvent
        {
            Controller = new StreamContracts.ControllerInput { ControllerIndex = 2, ControlId = 7, Value = -123 }
        });
        input.InputBatch.Events.Add(new StreamContracts.InputEvent
        {
            Touch = new StreamContracts.TouchInput
            {
                ContactId = 4,
                Action = StreamContracts.TouchAction.Move,
                XNumerator = 1,
                YNumerator = 2,
                CoordinateDenominator = 3,
                PressureNumerator = 4,
                PressureDenominator = 5
            }
        });
        return new WorkerIpcEnvelope
        {
            ProtocolVersion = ProtocolVersion.Current,
            SessionId = "session-a",
            InputReceived = new InputReceived { SessionGeneration = 17, Input = input }
        };
    }

    private static async Task WriteAsync(Stream stream, WorkerIpcEnvelope envelope)
    {
        byte[] frame = ProtobufLengthFrameCodec.Encode(envelope);
        await stream.WriteAsync(frame);
        await stream.FlushAsync();
    }

    private static async Task<WorkerIpcEnvelope> ReadAsync(Stream stream)
    {
        byte[] prefix = new byte[sizeof(uint)];
        await stream.ReadExactlyAsync(prefix);
        int messageLength = ProtobufLengthFrameCodec.ReadMessageLength(prefix);
        byte[] frame = new byte[sizeof(uint) + messageLength];
        prefix.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(sizeof(uint)));
        return ProtobufLengthFrameCodec.Decode(frame, WorkerIpcEnvelope.Parser);
    }

    private sealed class PipePair : IAsyncDisposable
    {
        private PipePair(NamedPipeServerStream service, NamedPipeClientStream worker)
        {
            Service = service;
            Worker = worker;
        }

        public NamedPipeServerStream Service { get; }

        public NamedPipeClientStream Worker { get; }

        public static async Task<PipePair> CreateAsync()
        {
            string name = $"beacon-worker-test-{Guid.NewGuid():N}";
            var service = new NamedPipeServerStream(
                name,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var worker = new NamedPipeClientStream(
                ".",
                name,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await Task.WhenAll(service.WaitForConnectionAsync(), worker.ConnectAsync());
            return new PipePair(service, worker);
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            await Worker.DisposeAsync();
        }
    }
}
