using System.IO.Pipes;
using System.Threading.Channels;
using Beacon.HostAgent.Contracts;
using Beacon.Platform.Windows.HostAgent;

namespace Beacon.Platform.Windows.Tests.HostAgent;

public sealed class HostAgentConnectionTests
{
    [Fact]
    public async Task HandshakePublishesStatusAndAllowsCorrelatedRequest()
    {
        var connector = new QueueConnector();
        await using var connection = new HostAgentConnection(connector);
        using var shutdown = new CancellationTokenSource();
        Task running = connection.RunAsync(shutdown.Token);
        await using ConnectedStreams pipes = await ConnectedStreams.CreateAsync();
        connector.Add(pipes.Client);
        Task agent = ServeHandshakeAndOneRequestAsync(pipes.Server, "first-agent");

        HostAgentConnectionState connected = await connection.WaitForStateChangeAsync(
            afterRevision: 0,
            CancellationToken.None);
        HostAgentResponse response = await connection.SendAsync(
            HostAgentOperation.QueryTopology,
            new EmptyHostAgentPayload(),
            CancellationToken.None);

        Assert.True(connected.Connected);
        Assert.Equal("first-agent", connected.Status?.DriverDiagnostic);
        Assert.True(response.Success);
        await agent;
        shutdown.Cancel();
        await running;
    }

    [Fact]
    public async Task PipeLossPublishesDisconnectedThenReconnectsWithoutDelayLoop()
    {
        var connector = new QueueConnector();
        await using var connection = new HostAgentConnection(connector);
        using var shutdown = new CancellationTokenSource();
        Task running = connection.RunAsync(shutdown.Token);
        await using ConnectedStreams first = await ConnectedStreams.CreateAsync();
        connector.Add(first.Client);
        Task firstAgent = ServeHandshakeAsync(first.Server, "first-agent");
        HostAgentConnectionState firstConnected = await connection.WaitForStateChangeAsync(
            0,
            CancellationToken.None);
        await firstAgent;

        await first.DisposeServerAsync();
        HostAgentConnectionState disconnected = await connection.WaitForStateChangeAsync(
            firstConnected.Revision,
            CancellationToken.None);

        await using ConnectedStreams second = await ConnectedStreams.CreateAsync();
        connector.Add(second.Client);
        Task secondAgent = ServeHandshakeAsync(second.Server, "second-agent");
        HostAgentConnectionState secondConnected = await connection.WaitForStateChangeAsync(
            disconnected.Revision,
            CancellationToken.None);

        Assert.False(disconnected.Connected);
        Assert.True(secondConnected.Connected);
        Assert.Equal("second-agent", secondConnected.Status?.DriverDiagnostic);
        Assert.Equal(2, connector.ConnectionCount);
        shutdown.Cancel();
        await running;
        await secondAgent;
    }

    [Fact]
    public async Task SendWhileDisconnectedFailsImmediately()
    {
        var connector = new QueueConnector();
        await using var connection = new HostAgentConnection(connector);

        await Assert.ThrowsAsync<HostAgentUnavailableException>(() => connection.SendAsync(
            HostAgentOperation.GetStatus,
            new EmptyHostAgentPayload(),
            CancellationToken.None));
    }

    private static async Task ServeHandshakeAndOneRequestAsync(Stream server, string diagnostic)
    {
        await ServeHandshakeAsync(server, diagnostic);
        HostAgentRequest request = await HostAgentFrameCodec.ReadRequestAsync(
            server,
            CancellationToken.None);
        await HostAgentFrameCodec.WriteResponseAsync(
            server,
            Success(request, new EmptyHostAgentPayload()),
            CancellationToken.None);
    }

    private static async Task ServeHandshakeAsync(Stream server, string diagnostic)
    {
        HostAgentRequest handshake = await HostAgentFrameCodec.ReadRequestAsync(
            server,
            CancellationToken.None);
        Assert.Equal(HostAgentOperation.GetStatus, handshake.Operation);
        var status = new HostAgentStatusPayload(
            DriverReady: true,
            DriverDiagnostic: diagnostic,
            new HostAgentLeaseSnapshotPayload(0, null, false, true, "idle"));
        await HostAgentFrameCodec.WriteResponseAsync(
            server,
            Success(handshake, status),
            CancellationToken.None);
    }

    private static HostAgentResponse Success<T>(HostAgentRequest request, T payload) =>
        new(
            HostAgentProtocol.CurrentVersion,
            request.RequestId,
            Success: true,
            ResultCode: "ok",
            Diagnostic: string.Empty,
            HostAgentProtocol.CreatePayload(payload));

    private sealed class QueueConnector : IHostAgentConnector
    {
        private readonly Channel<Stream> streams = Channel.CreateUnbounded<Stream>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

        public int ConnectionCount { get; private set; }

        public void Add(Stream stream)
        {
            Assert.True(streams.Writer.TryWrite(stream));
        }

        public async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
        {
            Stream stream = await streams.Reader.ReadAsync(cancellationToken);
            ConnectionCount++;
            return stream;
        }
    }

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
            string name = $"beacon-host-agent-supervisor-test-{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                name,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var client = new NamedPipeClientStream(
                ".",
                name,
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
        }
    }
}
