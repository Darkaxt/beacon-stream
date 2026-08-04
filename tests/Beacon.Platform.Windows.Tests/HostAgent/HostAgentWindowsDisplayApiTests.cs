using Beacon.HostAgent.Contracts;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.HostAgent;

namespace Beacon.Platform.Windows.Tests.HostAgent;

public sealed class HostAgentWindowsDisplayApiTests
{
    [Fact]
    public void DisconnectedAgentReportsUnavailableDriverAndLease()
    {
        var connection = new FakeHostAgentConnection(
            new HostAgentConnectionState(
                Revision: 1,
                Connected: false,
                Diagnostic: "Host Agent unavailable.",
                Status: null));
        var api = new HostAgentWindowsDisplayApi(connection);

        DisplayDriverStatus driver = api.GetDriverStatus();
        SudoVdaDriverLeaseSessionSnapshot lease = api.Snapshot;

        Assert.False(driver.Ready);
        Assert.Equal("Host Agent unavailable.", driver.Diagnostic);
        Assert.False(lease.Healthy);
        Assert.Equal("Host Agent unavailable.", lease.Diagnostic);
    }

    [Fact]
    public async Task CreateUsesTypedRequestAndCachesAgentDisplayName()
    {
        var connection = ReadyConnection();
        connection.Enqueue(Success(new CreateVirtualDisplayResultPayload("\\\\.\\DISPLAY7")));
        var api = new HostAgentWindowsDisplayApi(connection);

        DisplayApiResult result = await api.CreateVirtualDisplayAsync(
            "display-z-fold",
            2560,
            1600,
            120,
            CancellationToken.None);

        Assert.True(result.Success);
        HostAgentCall call = Assert.Single(connection.Calls);
        Assert.Equal(HostAgentOperation.CreateVirtualDisplay, call.Operation);
        Assert.Equal(
            new CreateVirtualDisplayPayload("display-z-fold", 2560, 1600, 120),
            Assert.IsType<CreateVirtualDisplayPayload>(call.Payload));
        Assert.True(api.TryResolveDisplayName("display-z-fold", out string? displayName));
        Assert.Equal("\\\\.\\DISPLAY7", displayName);
    }

    [Fact]
    public async Task QueryMapsTopologyAndRefreshesAgentDisplayNames()
    {
        var connection = ReadyConnection();
        connection.Enqueue(Success(
            new DisplayTopologyPayload(
                [
                    new DisplayPathPayload(
                        "physical",
                        HostAgentDisplayKind.Physical,
                        2560,
                        1600,
                        240,
                        IsPrimary: true,
                        X: 0,
                        Y: 0),
                    new DisplayPathPayload(
                        "display-z-fold",
                        HostAgentDisplayKind.Virtual,
                        2560,
                        1600,
                        120,
                        IsPrimary: false,
                        X: 2560,
                        Y: 0)
                ],
                IsMirrorMode: false,
                new Dictionary<string, string>
                {
                    ["display-z-fold"] = "\\\\.\\DISPLAY7"
                })));
        var api = new HostAgentWindowsDisplayApi(connection);

        DisplayTopologySnapshot topology = await api.QueryTopologyAsync(CancellationToken.None);

        Assert.False(topology.IsMirrorMode);
        Assert.Equal(2, topology.Paths.Count);
        Assert.Equal(DisplayPathKind.Physical, topology.Paths[0].Kind);
        Assert.Equal(DisplayPathKind.Virtual, topology.Paths[1].Kind);
        Assert.True(api.TryResolveDisplayName("display-z-fold", out string? displayName));
        Assert.Equal("\\\\.\\DISPLAY7", displayName);
    }

    [Fact]
    public async Task LeaseOperationsRefreshCachedSnapshot()
    {
        var connection = ReadyConnection();
        connection.Enqueue(Success(
            new HoldDisplayLeaseResultPayload(
                Acquired: true,
                new HostAgentLeaseSnapshotPayload(
                    LeaseCount: 1,
                    WatchdogTimeoutSeconds: 30,
                    HeartbeatActive: true,
                    Healthy: true,
                    Diagnostic: "Lease held."))));
        connection.Enqueue(Success(
            new HostAgentLeaseSnapshotPayload(
                LeaseCount: 0,
                WatchdogTimeoutSeconds: 30,
                HeartbeatActive: false,
                Healthy: true,
                Diagnostic: "No leases.")));
        var api = new HostAgentWindowsDisplayApi(connection);

        SudoVdaDriverLeaseHoldResult held = await api.HoldAsync(
            "display-z-fold",
            CancellationToken.None);
        Assert.True(held.Success);
        Assert.True(held.Acquired);
        Assert.Equal(1, api.Snapshot.LeaseCount);

        await api.ReleaseAsync("display-z-fold", CancellationToken.None);

        Assert.Equal(0, api.Snapshot.LeaseCount);
        Assert.Equal(
            [HostAgentOperation.HoldDisplayLease, HostAgentOperation.ReleaseDisplayLease],
            connection.Calls.Select(call => call.Operation));
    }

    [Fact]
    public async Task FallibleDisplayCommandPreservesAgentFailure()
    {
        var connection = ReadyConnection();
        connection.Enqueue(Failure("display-operation-failed", "Windows rejected topology."));
        var api = new HostAgentWindowsDisplayApi(connection);

        DisplayApiResult result = await api.SetVirtualPrimaryAsync(
            "display-z-fold",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Windows rejected topology.", result.Error);
    }

    [Fact]
    public async Task HdrCapabilityMapsAgentPayload()
    {
        var connection = ReadyConnection();
        connection.Enqueue(Success(
            new DisplayHdrCapabilityPayload(
                Supported: true,
                Enabled: false,
                Reason: "Supported but disabled.")));
        var api = new HostAgentWindowsDisplayApi(connection);

        DisplayHdrCapability result = await api.QueryHdrCapabilityAsync(
            "display-z-fold",
            CancellationToken.None);

        Assert.True(result.Supported);
        Assert.False(result.Enabled);
        Assert.Equal("Supported but disabled.", result.Reason);
    }

    [Fact]
    public async Task SetHdrStateUsesTypedRequest()
    {
        var connection = ReadyConnection();
        connection.Enqueue(Success(new EmptyHostAgentPayload()));
        var api = new HostAgentWindowsDisplayApi(connection);

        DisplayApiResult result = await api.SetHdrStateAsync(
            "display-z-fold",
            enabled: true,
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        HostAgentCall call = Assert.Single(connection.Calls);
        Assert.Equal(HostAgentOperation.SetHdrState, call.Operation);
        Assert.Equal(
            new SetHdrStatePayload("display-z-fold", Enabled: true),
            Assert.IsType<SetHdrStatePayload>(call.Payload));
    }

    private static FakeHostAgentConnection ReadyConnection() =>
        new(
            new HostAgentConnectionState(
                Revision: 1,
                Connected: true,
                Diagnostic: "Connected.",
                Status: new HostAgentStatusPayload(
                    DriverReady: true,
                    DriverDiagnostic: "Driver ready.",
                    Lease: new HostAgentLeaseSnapshotPayload(
                        LeaseCount: 0,
                        WatchdogTimeoutSeconds: 30,
                        HeartbeatActive: false,
                        Healthy: true,
                        Diagnostic: "No leases."))));

    private static HostAgentResponse Success<T>(T payload) =>
        new(
            HostAgentProtocol.CurrentVersion,
            Guid.NewGuid(),
            Success: true,
            ResultCode: "ok",
            Diagnostic: string.Empty,
            HostAgentProtocol.CreatePayload(payload));

    private static HostAgentResponse Failure(string resultCode, string diagnostic) =>
        new(
            HostAgentProtocol.CurrentVersion,
            Guid.NewGuid(),
            Success: false,
            resultCode,
            diagnostic,
            HostAgentProtocol.CreatePayload(new EmptyHostAgentPayload()));

    private sealed class FakeHostAgentConnection(HostAgentConnectionState state)
        : IHostAgentConnection
    {
        private readonly Queue<HostAgentResponse> responses = new();

        public HostAgentConnectionState State { get; } = state;

        public List<HostAgentCall> Calls { get; } = [];

        public void Enqueue(HostAgentResponse response) => responses.Enqueue(response);

        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<HostAgentResponse> SendAsync<TPayload>(
            HostAgentOperation operation,
            TPayload payload,
            CancellationToken cancellationToken)
        {
            Calls.Add(new HostAgentCall(operation, payload!));
            return Task.FromResult(responses.Dequeue());
        }

        public Task<HostAgentConnectionState> WaitForStateChangeAsync(
            long afterRevision,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record HostAgentCall(HostAgentOperation Operation, object Payload);
}
