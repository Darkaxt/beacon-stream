using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.IO.Pipes;
using System.Threading.Channels;
using Beacon.Platform.Windows.Streaming;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.Core.Streaming;
using Google.Protobuf;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class StreamWorkerProcessHostTests
{
    [Fact]
    public async Task HostWithoutEventSubscriptionFailsClosedBeforeBoundedBackpressure()
    {
        await using ConnectedStreams streams = await ConnectedStreams.CreateAsync();
        TestLaunch launch = TestLaunch.Waiting(streams.Service);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(streams.Worker, Hello(checked((uint)launch.Process.Id), 1));
            await WriteAsync(streams.Worker, Capabilities(1));
            await WriteAsync(streams.Worker, Ready(1));
            WorkerIpcEnvelope command = await ReadAsync(streams.Worker);
            for (int sequence = 1; sequence <= 3; sequence++)
            {
                await WriteAsync(streams.Worker, new WorkerIpcEnvelope
                {
                    ProtocolVersion = ProtocolVersion.Current,
                    SessionId = command.SessionId,
                    TransportAuthenticated = new TransportAuthenticated
                    {
                        SessionGeneration = checked((ulong)sequence),
                        MaximumDatagramBytes = 1200
                    }
                });
            }
            await WriteAsync(streams.Worker, Completion(command));
        });
        var host = new StreamWorkerProcessHost(
            new StreamWorkerProcessHostOptions("unused.exe", "unused.pfx"),
            new QueueLaunchFactory(launch),
            eventCapacity: 1);
        IStreamWorkerHost workerHost = host;
        try
        {
            await workerHost.EnsureReadyAsync(CancellationToken.None);
            long generation = workerHost.CurrentProcessGeneration;

            StreamWorkerProtocolException error = await Assert.ThrowsAsync<StreamWorkerProtocolException>(() =>
                workerHost.SendAsync(generation, Prepare("session"), CancellationToken.None));
            Exception? shutdownError = await Record.ExceptionAsync(() =>
                workerHost.ShutdownAsync(CancellationToken.None));
            try
            {
                await worker;
            }
            catch (Exception workerError) when (workerError is IOException or ObjectDisposedException)
            {
            }

            Assert.Equal("StreamWorker emitted an event without an active event subscription.", error.Message);
            Assert.Null(shutdownError);
            Assert.False(workerHost.IsReady);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExitBeforePipeConnectionPublishesOneGenerationExitEvent()
    {
        var launch = TestLaunch.Exiting(23);
        await using var host = new StreamWorkerProcessHost(
            new StreamWorkerProcessHostOptions("unused.exe", "unused.pfx"),
            new QueueLaunchFactory(launch));
        IStreamWorkerHost events = host;
        _ = events.Events;

        StreamWorkerProcessExitedException error = await Assert.ThrowsAsync<StreamWorkerProcessExitedException>(
            () => host.EnsureReadyAsync(CancellationToken.None));
        StreamWorkerProcessExited exited = Assert.IsType<StreamWorkerProcessExited>(
            await events.Events.ReadAsync());

        Assert.Equal(23, error.ExitCode);
        Assert.Equal(1, exited.ProcessGeneration);
        Assert.Equal(23, exited.ExitCode);
        Assert.False(events.Events.TryRead(out _));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("version")]
    [InlineData("ready")]
    public async Task InvalidHandshakePublishesOneExitEventForAssignedGeneration(string failure)
    {
        await using ConnectedStreams streams = await ConnectedStreams.CreateAsync();
        TestLaunch launch = TestLaunch.Waiting(streams.Service);
        Task worker = Task.Run(async () =>
        {
            uint processId = checked((uint)launch.Process.Id);
            if (failure == "hello")
            {
                await WriteAsync(streams.Worker, Hello(processId + 1, version: 1));
                return;
            }
            await WriteAsync(streams.Worker, Hello(processId, failure == "version" ? 2u : 1u));
            if (failure == "ready")
            {
                await WriteAsync(streams.Worker, Capabilities(version: 1));
                WorkerIpcEnvelope ready = Ready(version: 1);
                ready.WorkerReady.WorkerInstanceId = ByteString.CopyFrom(new byte[] { 9, 9, 9 });
                await WriteAsync(streams.Worker, ready);
            }
        });
        await using var host = new StreamWorkerProcessHost(
            new StreamWorkerProcessHostOptions("unused.exe", "unused.pfx"),
            new QueueLaunchFactory(launch));
        IStreamWorkerHost events = host;
        _ = events.Events;

        await Assert.ThrowsAnyAsync<Exception>(() => host.EnsureReadyAsync(CancellationToken.None));
        StreamWorkerProcessExited exited = Assert.IsType<StreamWorkerProcessExited>(
            await events.Events.ReadAsync());

        Assert.Equal(1, exited.ProcessGeneration);
        Assert.False(events.Events.TryRead(out _));
        await worker;
    }

    [Fact]
    public async Task DelayedHandshakeExitCannotInvalidateInitializedReplacement()
    {
        TestLaunch first = TestLaunch.Exiting(31);
        await using ConnectedStreams streams = await ConnectedStreams.CreateAsync();
        TestLaunch replacement = TestLaunch.Waiting(streams.Service);
        Task replacementWorker = Task.Run(async () =>
        {
            await WriteAsync(streams.Worker, Hello(checked((uint)replacement.Process.Id), 1));
            await WriteAsync(streams.Worker, Capabilities(1));
            await WriteAsync(streams.Worker, Ready(1));
        });
        var host = new StreamWorkerProcessHost(
            new StreamWorkerProcessHostOptions("unused.exe", "unused.pfx"),
            new QueueLaunchFactory(first, replacement));
        IStreamWorkerHost events = host;
        _ = events.Events;
        try
        {
            await Assert.ThrowsAsync<StreamWorkerProcessExitedException>(
                () => host.EnsureReadyAsync(CancellationToken.None));
            await host.EnsureReadyAsync(CancellationToken.None);

            StreamWorkerProcessExited oldExit = Assert.IsType<StreamWorkerProcessExited>(
                await events.Events.ReadAsync());

            Assert.Equal(1, oldExit.ProcessGeneration);
            Assert.Equal(2, events.CurrentProcessGeneration);
            Assert.True(events.IsCurrentProcessGeneration(2));
            Assert.True(host.IsReady);
            Assert.True(events.Capabilities.VideoAvailable);
            Assert.Equal(WorkerVideoEncoder.Nvenc, Assert.Single(events.Capabilities.VideoEncoders));
            await replacementWorker;
        }
        finally
        {
            replacement.Terminate();
            await replacement.Process.WaitForExitAsync();
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task GenerationBoundSendRejectsExitedProcessWithoutLaunchingReplacement()
    {
        await using ConnectedStreams streams = await ConnectedStreams.CreateAsync();
        TestLaunch launch = TestLaunch.Waiting(streams.Service);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(streams.Worker, Hello(checked((uint)launch.Process.Id), 1));
            await WriteAsync(streams.Worker, Capabilities(1));
            await WriteAsync(streams.Worker, Ready(1));
        });
        var factory = new QueueLaunchFactory(launch);
        var host = new StreamWorkerProcessHost(
            new StreamWorkerProcessHostOptions("unused.exe", "unused.pfx"),
            factory);
        IStreamWorkerHost generationHost = host;
        try
        {
            await host.EnsureReadyAsync(CancellationToken.None);
            long generation = generationHost.CurrentProcessGeneration;
            launch.Terminate();
            await launch.Process.WaitForExitAsync();

            StreamWorkerGenerationChangedException error =
                await Assert.ThrowsAsync<StreamWorkerGenerationChangedException>(() =>
                    generationHost.SendAsync(
                        generation,
                        Prepare("session"),
                        CancellationToken.None));

            Assert.Equal(generation, error.ExpectedProcessGeneration);
            Assert.Equal(1, factory.LaunchCount);
            Assert.Equal(generation, generationHost.CurrentProcessGeneration);
            await worker;
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReadyHostDisposeGracefullyShutsDownThenCompletesEventChannel()
    {
        await using ConnectedStreams streams = await ConnectedStreams.CreateAsync();
        TestLaunch launch = TestLaunch.Waiting(streams.Service);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(streams.Worker, Hello(checked((uint)launch.Process.Id), 1));
            await WriteAsync(streams.Worker, Capabilities(1));
            await WriteAsync(streams.Worker, Ready(1));
            WorkerIpcEnvelope shutdown = await ReadAsync(streams.Worker);
            Assert.Equal(WorkerIpcEnvelope.BodyOneofCase.ShutdownWorker, shutdown.BodyCase);
            await WriteAsync(streams.Worker, new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                RequestId = shutdown.RequestId,
                WorkerCompletion = new WorkerCompletion
                {
                    Succeeded = true,
                    ErrorCode = WorkerErrorCode.None
                }
            });
            launch.Terminate();
        });
        var host = new StreamWorkerProcessHost(
            new StreamWorkerProcessHostOptions("unused.exe", "unused.pfx"),
            new QueueLaunchFactory(launch));
        IStreamWorkerHost eventSource = host;
        _ = eventSource.Events;
        await host.EnsureReadyAsync(CancellationToken.None);

        Exception? error = await Record.ExceptionAsync(() => host.DisposeAsync().AsTask());
        await worker;

        Assert.Null(error);
        Assert.IsType<StreamWorkerProcessExited>(await eventSource.Events.ReadAsync());
        await eventSource.Events.Completion;
        Assert.True(eventSource.Events.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ExceptionalDisposeReleasesWorkerAndEventChannelExactlyOnce()
    {
        await using ConnectedStreams streams = await ConnectedStreams.CreateAsync();
        TestLaunch launch = TestLaunch.Waiting(streams.Service);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(streams.Worker, Hello(checked((uint)launch.Process.Id), 1));
            await WriteAsync(streams.Worker, Capabilities(1));
            await WriteAsync(streams.Worker, Ready(1));
            WorkerIpcEnvelope shutdown = await ReadAsync(streams.Worker);
            await WriteAsync(streams.Worker, new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                RequestId = shutdown.RequestId,
                WorkerCompletion = new WorkerCompletion
                {
                    Succeeded = false,
                    ErrorCode = WorkerErrorCode.OperationFailed
                }
            });
        });
        var host = new StreamWorkerProcessHost(
            new StreamWorkerProcessHostOptions("unused.exe", "unused.pfx"),
            new QueueLaunchFactory(launch));
        IStreamWorkerHost eventSource = host;
        _ = eventSource.Events;
        await host.EnsureReadyAsync(CancellationToken.None);

        await Assert.ThrowsAsync<StreamWorkerProtocolException>(() => host.DisposeAsync().AsTask());
        await worker;

        Assert.Equal(1, launch.TerminateCalls);
        Assert.IsType<StreamWorkerProcessExited>(await eventSource.Events.ReadAsync());
        await eventSource.Events.Completion;
        Assert.True(eventSource.Events.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DisposeAwaitsSchedulingDelayedExitPublicationBeforeCompletingEvents()
    {
        await using ConnectedStreams streams = await ConnectedStreams.CreateAsync();
        TestLaunch launch = TestLaunch.Waiting(streams.Service);
        var publicationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowPublication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = RunGracefulShutdownWorkerAsync(streams.Worker, launch);
        var host = new StreamWorkerProcessHost(
            new StreamWorkerProcessHostOptions("unused.exe", "unused.pfx"),
            new QueueLaunchFactory(launch),
            eventCapacity: 64,
            beforeProcessExitPublication: async _ =>
            {
                publicationEntered.SetResult();
                await allowPublication.Task;
            });
        IStreamWorkerHost eventSource = host;
        _ = eventSource.Events;
        await host.EnsureReadyAsync(CancellationToken.None);

        Task disposal = host.DisposeAsync().AsTask();
        await publicationEntered.Task;

        Assert.False(disposal.IsCompleted);
        Assert.False(eventSource.Events.Completion.IsCompleted);

        allowPublication.SetResult();
        Assert.IsType<StreamWorkerProcessExited>(await eventSource.Events.ReadAsync());
        await disposal;
        await worker;
        await eventSource.Events.Completion;
    }

    [Fact]
    public async Task DisposeAwaitsBackpressuredExitPublicationBeforeCompletingEvents()
    {
        await using ConnectedStreams streams = await ConnectedStreams.CreateAsync();
        TestLaunch launch = TestLaunch.Waiting(streams.Service);
        var publicationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowPublication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exitWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(streams.Worker, Hello(checked((uint)launch.Process.Id), 1));
            await WriteAsync(streams.Worker, Capabilities(1));
            await WriteAsync(streams.Worker, Ready(1));
            WorkerIpcEnvelope prepare = await ReadAsync(streams.Worker);
            await WriteAsync(streams.Worker, new WorkerIpcEnvelope
            {
                ProtocolVersion = ProtocolVersion.Current,
                SessionId = prepare.SessionId,
                TransportAuthenticated = new TransportAuthenticated
                {
                    SessionGeneration = 1,
                    MaximumDatagramBytes = 1200
                }
            });
            await WriteAsync(streams.Worker, Completion(prepare));
            WorkerIpcEnvelope shutdown = await ReadAsync(streams.Worker);
            await WriteAsync(streams.Worker, Completion(shutdown));
            launch.Terminate();
        });
        var host = new StreamWorkerProcessHost(
            new StreamWorkerProcessHostOptions("unused.exe", "unused.pfx"),
            new QueueLaunchFactory(launch),
            eventCapacity: 1,
            beforeProcessExitPublication: async _ =>
            {
                publicationEntered.SetResult();
                await allowPublication.Task;
            },
            processExitPublicationStarted: _ => exitWriteStarted.SetResult());
        IStreamWorkerHost eventSource = host;
        _ = eventSource.Events;
        await host.EnsureReadyAsync(CancellationToken.None);
        _ = await eventSource.SendAsync(
            eventSource.CurrentProcessGeneration,
            Prepare("session"),
            CancellationToken.None);

        Task disposal = host.DisposeAsync().AsTask();
        await publicationEntered.Task;
        allowPublication.SetResult();
        await exitWriteStarted.Task;

        Assert.False(disposal.IsCompleted);
        Assert.False(eventSource.Events.Completion.IsCompleted);
        Assert.IsType<StreamWorkerTransportAuthenticated>(await eventSource.Events.ReadAsync());
        Assert.IsType<StreamWorkerProcessExited>(await eventSource.Events.ReadAsync());
        await disposal;
        await worker;
        await eventSource.Events.Completion;
    }

    [Fact]
    public async Task EventReaderIsStableAcrossMonotonicWorkerReplacement()
    {
        await using var events = new StreamWorkerEventBuffer(capacity: 2);
        ChannelReader<StreamWorkerEvent> reader = events.Reader;

        long first = events.ActivateNextGeneration();
        long second = events.ActivateNextGeneration();

        Assert.Same(reader, events.Reader);
        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.False(events.IsCurrentGeneration(first));
        Assert.True(events.IsCurrentGeneration(second));
        Assert.False(reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task OldProcessExitIsPublishedOnceWithoutInvalidatingReplacement()
    {
        await using var events = new StreamWorkerEventBuffer(capacity: 2);
        _ = events.Reader;
        long oldGeneration = events.ActivateNextGeneration();
        long replacementGeneration = events.ActivateNextGeneration();

        await events.PublishProcessExitedAsync(oldGeneration, 23);
        await events.PublishProcessExitedAsync(oldGeneration, 23);

        StreamWorkerProcessExited exited = Assert.IsType<StreamWorkerProcessExited>(
            await events.Reader.ReadAsync());
        Assert.Equal(oldGeneration, exited.ProcessGeneration);
        Assert.Equal(23, exited.ExitCode);
        Assert.False(events.Reader.TryRead(out _));
        Assert.Equal(replacementGeneration, events.CurrentGeneration);
        Assert.True(events.IsCurrentGeneration(replacementGeneration));
    }

    [Fact]
    public async Task DisposalCompletesStableEventChannelAndCancelsBlockedWriter()
    {
        var events = new StreamWorkerEventBuffer(capacity: 1);
        _ = events.Reader;
        long generation = events.ActivateNextGeneration();
        await events.PublishProcessExitedAsync(generation, 1);
        Task blocked = events.PublishProcessExitedAsync(generation + 1, 2).AsTask();

        await events.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        _ = await events.Reader.ReadAsync();
        await events.Reader.Completion;
        Assert.True(events.Reader.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task UnsubscribedEventBufferNeverBackpressuresExitPublication()
    {
        await using var events = new StreamWorkerEventBuffer(capacity: 1);

        Assert.False(events.HasSubscriber);
        for (int generation = 1; generation <= 3; generation++)
        {
            await events.PublishProcessExitedAsync(generation, generation);
        }
        Assert.False(events.HasSubscriber);
    }
    [Fact]
    public void OptionsUseExplicitWorkerExecutablePath()
    {
        string executablePath = Path.Combine(Path.GetTempPath(), "acceptance", "Beacon.StreamWorker.exe");
        string identityPath = Path.Combine(Path.GetTempPath(), "identity.pfx");

        StreamWorkerProcessHostOptions options = StreamWorkerProcessHostOptions.Create(
            executablePath,
            identityPath);

        Assert.Equal(executablePath, options.ExecutablePath);
        Assert.Equal(identityPath, options.IdentityPath);
    }

    [Fact]
    public void OptionsDefaultWorkerExecutablePathToApplicationDirectory()
    {
        string identityPath = Path.Combine(Path.GetTempPath(), "identity.pfx");

        StreamWorkerProcessHostOptions options = StreamWorkerProcessHostOptions.Create(
            executablePath: null,
            identityPath);

        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "Beacon.StreamWorker.exe"),
            options.ExecutablePath);
        Assert.Equal(identityPath, options.IdentityPath);
    }

    [Fact]
    public void CreateDefaultDelegatesToDefaultWorkerExecutablePath()
    {
        string identityPath = Path.Combine(Path.GetTempPath(), "identity.pfx");

        StreamWorkerProcessHostOptions options = StreamWorkerProcessHostOptions.CreateDefault(identityPath);

        Assert.Equal(StreamWorkerProcessHostOptions.Create(null, identityPath), options);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("C:\\Program Files\\Beacon\\", "\"C:\\Program Files\\Beacon\\\\\"")]
    [InlineData("quoted\"value", "\"quoted\\\"value\"")]
    public void ActiveSessionArgumentsUseWindowsCommandLineQuoting(string value, string expected)
    {
        Assert.Equal(expected, InteractiveStreamWorkerLauncher.QuoteArgument(value));
    }

    [Fact]
    public void PipeSecurityAllowsOnlyOwningUserAndLocalSystem()
    {
        var owner = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        PipeSecurity security = StreamWorkerProcessHost.CreatePipeSecurity(owner);

        Assert.True(security.AreAccessRulesProtected);
        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: false,
            typeof(SecurityIdentifier));
        PipeAccessRule[] access = rules.Cast<PipeAccessRule>().ToArray();
        Assert.Equal(2, access.Length);
        Assert.All(access, rule => Assert.Equal(AccessControlType.Allow, rule.AccessControlType));
        Assert.Contains(access, rule => owner.Equals(rule.IdentityReference));
        Assert.Contains(access, rule =>
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Equals(rule.IdentityReference));
    }

    [Fact]
    public async Task RealWorkerCompletesExplicitLifecycleWhenBinaryIsAvailable()
    {
        string? executable = Environment.GetEnvironmentVariable("BEACON_STREAM_WORKER_PATH");
        string? identity = Environment.GetEnvironmentVariable("BEACON_SERVER_IDENTITY_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)
            || string.IsNullOrWhiteSpace(identity) || !File.Exists(identity))
        {
            return;
        }

        await using var host = new StreamWorkerProcessHost(
            new StreamWorkerProcessHostOptions(executable, identity));

        await host.EnsureReadyAsync(CancellationToken.None);
        int processId = host.ProcessId;
        var authorizer = new StreamWorkerSessionAuthorizer(host);
        StreamRuntimeAuthorizationContext authorizationContext =
            await authorizer.GetContextAsync(CancellationToken.None);
        StreamWorkerCommandResponse prepare = await host.SendAsync(
            authorizationContext.RuntimeGeneration,
            Prepare("integration-session"),
            CancellationToken.None);
        Assert.True(
            prepare.Completion.WorkerCompletion.Succeeded,
            $"StreamWorker prepare failed ({prepare.Completion.WorkerCompletion.ErrorCode}).");
        StreamRuntimeAuthorizationResult authorization = await authorizer.AuthorizeAsync(
            new StreamRuntimeAuthorization(
                "integration-session",
                "z-fold-7",
                1,
                Enumerable.Repeat((byte)0x5a, 32).ToArray(),
                authorizationContext.RuntimeInstanceId,
                authorizationContext.RuntimeGeneration,
                DateTimeOffset.UtcNow.AddMinutes(2)),
            CancellationToken.None);
        Assert.True(authorization.Success, authorization.Error);
        StreamRuntimeAuthorizationResult revocation = await authorizer.RevokeAsync(
            new StreamRuntimeRevocation(
                "integration-session",
                Enumerable.Repeat((byte)0x5a, 32).ToArray(),
                authorizationContext.RuntimeGeneration),
            CancellationToken.None);
        Assert.True(revocation.Success, revocation.Error);
        StreamWorkerCommandResponse start = await host.SendAsync(
            authorizationContext.RuntimeGeneration,
            new WorkerIpcEnvelope
            {
                SessionId = "integration-session",
                StartMedia = new StartMedia(),
            },
            CancellationToken.None);
        Assert.True(
            start.Completion.WorkerCompletion.Succeeded,
            $"StreamWorker start failed ({start.Completion.WorkerCompletion.ErrorCode}).");
        StreamWorkerCommandResponse stop = await host.SendAsync(
            authorizationContext.RuntimeGeneration,
            new WorkerIpcEnvelope
            {
                SessionId = "integration-session",
                StopMedia = new StopMedia { Reason = StopMediaReason.Explicit },
            },
            CancellationToken.None);
        Assert.True(
            stop.Completion.WorkerCompletion.Succeeded,
            $"StreamWorker stop failed ({stop.Completion.WorkerCompletion.ErrorCode}).");

        Assert.True(host.IsReady);
        Assert.Equal(0ul, start.Events.Single(e => e.BodyCase == WorkerIpcEnvelope.BodyOneofCase.MediaMetrics)
            .MediaMetrics.EncodedFrames);

        using (System.Diagnostics.Process firstWorker = System.Diagnostics.Process.GetProcessById(processId))
        {
            firstWorker.Kill();
            await firstWorker.WaitForExitAsync();
        }

        await host.EnsureReadyAsync(CancellationToken.None);

        Assert.True(host.IsReady);
        Assert.NotEqual(processId, host.ProcessId);

        await host.ShutdownAsync(CancellationToken.None);

        Assert.False(host.IsReady);
        Assert.True(host.HasExited);
        Assert.DoesNotContain(System.Diagnostics.Process.GetProcesses(), process => process.Id == processId);
    }

    private static WorkerIpcEnvelope Prepare(string sessionId) => new()
    {
        SessionId = sessionId,
        PrepareSession = new PrepareSession
        {
            DisplayTarget = "virtual-test",
            DisplayDeviceName = @"\\.\DISPLAY7",
            VideoCodec = WorkerVideoCodec.H264,
            Width = 2560,
            Height = 1600,
            FramesPerSecondNumerator = 120,
            FramesPerSecondDenominator = 1,
            DynamicRange = WorkerDynamicRange.Sdr,
            MinimumBitrateKbps = 1000,
            InitialBitrateKbps = 45000,
            MaximumBitrateKbps = 90000,
            AudioCodec = WorkerAudioCodec.Opus,
            AudioSampleRateHz = 48_000,
            AudioChannelCount = 2,
            AudioFrameDurationUs = 20_000,
            AudioBitrateBps = 96_000,
        },
    };

    private static WorkerIpcEnvelope Hello(uint processId, uint version) => new()
    {
        ProtocolVersion = version,
        WorkerHello = new WorkerHello
        {
            ProcessId = processId,
            WorkerInstanceId = ByteString.CopyFrom(new byte[] { 1, 2, 3 })
        }
    };

    private static WorkerIpcEnvelope Ready(uint version) => new()
    {
        ProtocolVersion = version,
        WorkerReady = new WorkerReady
        {
            WorkerInstanceId = ByteString.CopyFrom(new byte[] { 1, 2, 3 })
        }
    };

    private static WorkerIpcEnvelope Capabilities(uint version)
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
                VideoAvailable = true
            }
        };
        envelope.WorkerCapabilities.VideoCodecs.Add(WorkerVideoCodec.H264);
        envelope.WorkerCapabilities.VideoEncoders.Add(WorkerVideoEncoder.Nvenc);
        envelope.WorkerCapabilities.CaptureMethods.Add(
            WorkerCaptureMethod.WindowsGraphicsCapture);
        return envelope;
    }

    private static async Task WriteAsync(Stream stream, WorkerIpcEnvelope envelope)
    {
        await stream.WriteAsync(ProtobufLengthFrameCodec.Encode(envelope));
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

    private static WorkerIpcEnvelope Completion(WorkerIpcEnvelope request) => new()
    {
        ProtocolVersion = ProtocolVersion.Current,
        RequestId = request.RequestId,
        SessionId = request.SessionId,
        WorkerCompletion = new WorkerCompletion
        {
            Succeeded = true,
            ErrorCode = WorkerErrorCode.None
        }
    };

    private static Task RunGracefulShutdownWorkerAsync(Stream stream, TestLaunch launch) => Task.Run(async () =>
    {
        await WriteAsync(stream, Hello(checked((uint)launch.Process.Id), 1));
        await WriteAsync(stream, Capabilities(1));
        await WriteAsync(stream, Ready(1));
        WorkerIpcEnvelope shutdown = await ReadAsync(stream);
        await WriteAsync(stream, Completion(shutdown));
        launch.Terminate();
    });

    private sealed class QueueLaunchFactory(params IStreamWorkerLaunch[] launches) : IStreamWorkerLaunchFactory
    {
        private readonly Queue<IStreamWorkerLaunch> remaining = new(launches);

        public int LaunchCount { get; private set; }

        public IStreamWorkerLaunch Launch(StreamWorkerProcessHostOptions options)
        {
            LaunchCount++;
            return remaining.Dequeue();
        }
    }

    private sealed class TestLaunch : IStreamWorkerLaunch
    {
        private readonly Stream? stream;
        private readonly TaskCompletionSource<Stream> connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposed;

        private TestLaunch(Process process, Stream? stream)
        {
            Process = process;
            this.stream = stream;
            if (stream is not null)
            {
                connection.TrySetResult(stream);
            }
        }

        public Process Process { get; }

        public int TerminateCalls { get; private set; }

        public static TestLaunch Exiting(int exitCode) =>
            new(StartPowerShell($"exit {exitCode}"), stream: null);

        public static TestLaunch Waiting(Stream stream) =>
            new(StartPowerShell("[Console]::In.ReadLine() | Out-Null", redirectInput: true), stream);

        public Task<Stream> ConnectAsync(CancellationToken cancellationToken) =>
            connection.Task.WaitAsync(cancellationToken);

        public void Terminate()
        {
            TerminateCalls++;
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            if (!Process.HasExited)
            {
                await Process.WaitForExitAsync();
            }
            Process.Dispose();
        }

        private static Process StartPowerShell(string command, bool redirectInput = false)
        {
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = redirectInput,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(command);
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException("Test process did not start.");
        }
    }

    private sealed class ConnectedStreams : IAsyncDisposable
    {
        private ConnectedStreams(NamedPipeServerStream service, NamedPipeClientStream worker)
        {
            Service = service;
            Worker = worker;
        }

        public NamedPipeServerStream Service { get; }

        public NamedPipeClientStream Worker { get; }

        public static async Task<ConnectedStreams> CreateAsync()
        {
            string name = $"beacon-host-test-{Guid.NewGuid():N}";
            var service = new NamedPipeServerStream(
                name,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var worker = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(service.WaitForConnectionAsync(), worker.ConnectAsync());
            return new ConnectedStreams(service, worker);
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            await Worker.DisposeAsync();
        }
    }
}
