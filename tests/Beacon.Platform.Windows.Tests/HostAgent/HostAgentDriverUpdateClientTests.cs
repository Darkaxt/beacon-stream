using Beacon.HostAgent.Contracts;
using Beacon.Platform.Windows.Displays;
using Beacon.Platform.Windows.HostAgent;

namespace Beacon.Platform.Windows.Tests.HostAgent;

public sealed class HostAgentDriverUpdateClientTests
{
    [Fact]
    public async Task StartSendsPackageTransactionAndCurrentServiceLeaseCount()
    {
        var connection = new FakeConnection();
        var leases = new FakeLeaseSession
        {
            Snapshot = new SudoVdaDriverLeaseSessionSnapshot(2, 30, true, true, "held")
        };
        Guid transactionId = Guid.NewGuid();
        connection.Response = Success(new SudoVdaUpdatePayload(
            transactionId,
            "sudovda-next",
            SudoVdaUpdateState.Accepted,
            "driver-update-accepted",
            PreviousEvidence: null,
            ActiveEvidence: null));
        var client = new HostAgentDriverUpdateClient(connection, leases);

        SudoVdaUpdatePayload result = await client.StartAsync(
            "sudovda-next",
            transactionId,
            CancellationToken.None);

        Assert.Equal(SudoVdaUpdateState.Accepted, result.State);
        HostAgentCall call = Assert.Single(connection.Calls);
        Assert.Equal(HostAgentOperation.InstallStagedSudoVdaPackage, call.Operation);
        Assert.Equal(
            new InstallStagedSudoVdaPackagePayload("sudovda-next", transactionId, 2),
            Assert.IsType<InstallStagedSudoVdaPackagePayload>(call.Payload));
    }

    [Fact]
    public async Task QueryMapsDurableTransactionResponse()
    {
        var connection = new FakeConnection();
        Guid transactionId = Guid.NewGuid();
        connection.Response = Success(new SudoVdaUpdatePayload(
            transactionId,
            "sudovda-next",
            SudoVdaUpdateState.Succeeded,
            "driver-update-succeeded",
            PreviousEvidence: null,
            ActiveEvidence: null));
        var client = new HostAgentDriverUpdateClient(connection, new FakeLeaseSession());

        SudoVdaUpdatePayload result = await client.QueryAsync(
            transactionId,
            CancellationToken.None);

        Assert.Equal(SudoVdaUpdateState.Succeeded, result.State);
        Assert.Equal(
            new QuerySudoVdaUpdatePayload(transactionId),
            Assert.IsType<QuerySudoVdaUpdatePayload>(Assert.Single(connection.Calls).Payload));
    }

    [Fact]
    public async Task AgentFailurePreservesStableResultCodeAndDiagnostic()
    {
        var connection = new FakeConnection
        {
            Response = new HostAgentResponse(
                HostAgentProtocol.CurrentVersion,
                Guid.NewGuid(),
                Success: false,
                ResultCode: "driver-update-busy",
                Diagnostic: "Another driver update transaction is active.",
                HostAgentProtocol.CreatePayload(new EmptyHostAgentPayload()))
        };
        var client = new HostAgentDriverUpdateClient(connection, new FakeLeaseSession());

        HostAgentDriverUpdateException error = await Assert.ThrowsAsync<
            HostAgentDriverUpdateException>(() => client.StartAsync(
                "sudovda-next",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("driver-update-busy", error.ResultCode);
        Assert.Equal("Another driver update transaction is active.", error.Message);
    }

    private static HostAgentResponse Success<T>(T payload) =>
        new(
            HostAgentProtocol.CurrentVersion,
            Guid.NewGuid(),
            Success: true,
            ResultCode: "ok",
            Diagnostic: string.Empty,
            HostAgentProtocol.CreatePayload(payload));

    private sealed class FakeConnection : IHostAgentConnection
    {
        public HostAgentConnectionState State { get; } = new(
            Revision: 1,
            Connected: true,
            Diagnostic: "connected",
            Status: null);

        public HostAgentResponse Response { get; set; } = Success(
            new EmptyHostAgentPayload());

        public List<HostAgentCall> Calls { get; } = [];

        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<HostAgentResponse> SendAsync<TPayload>(
            HostAgentOperation operation,
            TPayload payload,
            CancellationToken cancellationToken)
        {
            Calls.Add(new HostAgentCall(operation, payload!));
            return Task.FromResult(Response);
        }

        public Task<HostAgentConnectionState> WaitForStateChangeAsync(
            long afterRevision,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLeaseSession : IWindowsDisplayLeaseSession
    {
        public SudoVdaDriverLeaseSessionSnapshot Snapshot { get; init; } =
            new(0, null, false, true, "idle");

        public Task<SudoVdaDriverLeaseHoldResult> HoldAsync(
            string displayId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleaseAsync(
            string displayId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed record HostAgentCall(HostAgentOperation Operation, object Payload);
}
