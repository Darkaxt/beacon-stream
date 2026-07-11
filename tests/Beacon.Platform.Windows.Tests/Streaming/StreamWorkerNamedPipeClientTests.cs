using System.Buffers.Binary;
using System.IO.Pipes;
using Beacon.Platform.Windows.Streaming;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Google.Protobuf;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class StreamWorkerNamedPipeClientTests
{
    [Fact]
    public async Task InitializeRequiresMatchingHelloAndReadyMessages()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(processId: 42, version: 1));
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
    public async Task OversizedFrameFailsPendingRequestBeforePayloadAllocation()
    {
        await using PipePair pipes = await PipePair.CreateAsync();
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.Run(async () =>
        {
            await WriteAsync(pipes.Worker, Hello(42, 1));
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
