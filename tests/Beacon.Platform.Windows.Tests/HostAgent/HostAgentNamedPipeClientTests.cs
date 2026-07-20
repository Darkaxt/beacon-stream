using System.IO.Pipes;
using Beacon.HostAgent.Contracts;
using Beacon.Platform.Windows.HostAgent;

namespace Beacon.Platform.Windows.Tests.HostAgent;

public sealed class HostAgentNamedPipeClientTests
{
    [Fact]
    public async Task ConcurrentRequestsAreCorrelatedByRequestId()
    {
        await using ConnectedStreams pipes = await ConnectedStreams.CreateAsync();
        await using var client = new HostAgentNamedPipeClient(pipes.Client);
        Task<HostAgentResponse> first = client.SendAsync(
            HostAgentOperation.GetStatus,
            new EmptyHostAgentPayload(),
            CancellationToken.None);
        Task<HostAgentResponse> second = client.SendAsync(
            HostAgentOperation.QueryTopology,
            new EmptyHostAgentPayload(),
            CancellationToken.None);
        HostAgentRequest firstRequest = await HostAgentFrameCodec.ReadRequestAsync(
            pipes.Server,
            CancellationToken.None);
        HostAgentRequest secondRequest = await HostAgentFrameCodec.ReadRequestAsync(
            pipes.Server,
            CancellationToken.None);

        await HostAgentFrameCodec.WriteResponseAsync(
            pipes.Server,
            Success(secondRequest, "second"),
            CancellationToken.None);
        await HostAgentFrameCodec.WriteResponseAsync(
            pipes.Server,
            Success(firstRequest, "first"),
            CancellationToken.None);

        Assert.Equal("first", (await first).Diagnostic);
        Assert.Equal("second", (await second).Diagnostic);
    }

    [Fact]
    public async Task PipeLossFailsPendingRequest()
    {
        await using ConnectedStreams pipes = await ConnectedStreams.CreateAsync();
        await using var client = new HostAgentNamedPipeClient(pipes.Client);
        Task<HostAgentResponse> pending = client.SendAsync(
            HostAgentOperation.GetStatus,
            new EmptyHostAgentPayload(),
            CancellationToken.None);
        _ = await HostAgentFrameCodec.ReadRequestAsync(pipes.Server, CancellationToken.None);

        await pipes.DisposeServerAsync();

        await Assert.ThrowsAsync<HostAgentConnectionLostException>(() => pending);
        await Assert.ThrowsAsync<HostAgentConnectionLostException>(() => client.Completion);
    }

    [Fact]
    public async Task UnknownResponseTerminatesConnectionAndFailsPendingRequest()
    {
        await using ConnectedStreams pipes = await ConnectedStreams.CreateAsync();
        await using var client = new HostAgentNamedPipeClient(pipes.Client);
        Task<HostAgentResponse> pending = client.SendAsync(
            HostAgentOperation.GetStatus,
            new EmptyHostAgentPayload(),
            CancellationToken.None);
        HostAgentRequest request = await HostAgentFrameCodec.ReadRequestAsync(
            pipes.Server,
            CancellationToken.None);
        HostAgentResponse unknown = Success(request with { RequestId = Guid.NewGuid() }, "unknown");

        await HostAgentFrameCodec.WriteResponseAsync(
            pipes.Server,
            unknown,
            CancellationToken.None);

        await Assert.ThrowsAsync<HostAgentProtocolException>(() => pending);
        await Assert.ThrowsAsync<HostAgentProtocolException>(() => client.Completion);
    }

    [Fact]
    public async Task CallerCancellationDoesNotTerminateConnection()
    {
        await using ConnectedStreams pipes = await ConnectedStreams.CreateAsync();
        await using var client = new HostAgentNamedPipeClient(pipes.Client);
        using var cancellation = new CancellationTokenSource();
        Task<HostAgentResponse> canceled = client.SendAsync(
            HostAgentOperation.GetStatus,
            new EmptyHostAgentPayload(),
            cancellation.Token);
        HostAgentRequest canceledRequest = await HostAgentFrameCodec.ReadRequestAsync(
            pipes.Server,
            CancellationToken.None);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        await HostAgentFrameCodec.WriteResponseAsync(
            pipes.Server,
            Success(canceledRequest, "late"),
            CancellationToken.None);

        Task<HostAgentResponse> next = client.SendAsync(
            HostAgentOperation.QueryTopology,
            new EmptyHostAgentPayload(),
            CancellationToken.None);
        HostAgentRequest nextRequest = await HostAgentFrameCodec.ReadRequestAsync(
            pipes.Server,
            CancellationToken.None);
        await HostAgentFrameCodec.WriteResponseAsync(
            pipes.Server,
            Success(nextRequest, "next"),
            CancellationToken.None);

        Assert.Equal("next", (await next).Diagnostic);
    }

    private static HostAgentResponse Success(HostAgentRequest request, string diagnostic) =>
        new(
            HostAgentProtocol.CurrentVersion,
            request.RequestId,
            Success: true,
            ResultCode: "ok",
            diagnostic,
            HostAgentProtocol.CreatePayload(new EmptyHostAgentPayload()));

    private sealed class ConnectedStreams : IAsyncDisposable
    {
        private NamedPipeServerStream? server;

        private ConnectedStreams(NamedPipeServerStream server, NamedPipeClientStream client)
        {
            this.server = server;
            Client = client;
        }

        public NamedPipeServerStream Server =>
            server ?? throw new ObjectDisposedException(nameof(ConnectedStreams));

        public NamedPipeClientStream Client { get; }

        public static async Task<ConnectedStreams> CreateAsync()
        {
            string pipeName = $"beacon-host-agent-client-test-{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            Task connect = client.ConnectAsync(CancellationToken.None);
            await server.WaitForConnectionAsync(CancellationToken.None);
            await connect;
            return new ConnectedStreams(server, client);
        }

        public async Task DisposeServerAsync()
        {
            NamedPipeServerStream? owned = Interlocked.Exchange(ref server, null);
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeServerAsync();
            await Client.DisposeAsync();
        }
    }
}
