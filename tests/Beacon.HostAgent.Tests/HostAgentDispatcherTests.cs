using Beacon.HostAgent.Contracts;
using Beacon.HostAgent.DriverUpdates;
using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent.Tests;

public sealed class HostAgentDispatcherTests
{
    [Fact]
    public async Task ProtocolMismatchIsRejectedBeforeExecutorUse()
    {
        var executor = new FakeDisplayExecutor();
        var dispatcher = new HostAgentDispatcher(executor);
        HostAgentRequest request = Request(
            HostAgentOperation.GetStatus,
            new EmptyHostAgentPayload()) with
        {
            ProtocolVersion = HostAgentProtocol.CurrentVersion + 1
        };

        HostAgentResponse response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("protocol-mismatch", response.ResultCode);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task GetStatusReturnsDriverAndLeaseEvidence()
    {
        var executor = new FakeDisplayExecutor
        {
            DriverStatus = new DisplayDriverStatus(true, "driver-ready"),
            LeaseSnapshot = new SudoVdaDriverLeaseSessionSnapshot(1, 10, true, true, "lease-ready")
        };
        var dispatcher = new HostAgentDispatcher(executor);

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(HostAgentOperation.GetStatus, new EmptyHostAgentPayload()),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("ok", response.ResultCode);
        HostAgentStatusPayload payload = HostAgentProtocol.ReadPayload<HostAgentStatusPayload>(
            response.Payload);
        Assert.True(payload.DriverReady);
        Assert.Equal("driver-ready", payload.DriverDiagnostic);
        Assert.Equal(1, payload.Lease.LeaseCount);
        Assert.Equal((uint)10, payload.Lease.WatchdogTimeoutSeconds);
        Assert.True(payload.Lease.HeartbeatActive);
        Assert.True(payload.Lease.Healthy);
        Assert.Equal("lease-ready", payload.Lease.Diagnostic);
        Assert.Equal(new[] { "status", "snapshot" }, executor.Calls);
    }

    [Fact]
    public async Task HoldDisplayLeaseReturnsAcquisitionAndUpdatedSnapshot()
    {
        var executor = new FakeDisplayExecutor
        {
            HoldResult = SudoVdaDriverLeaseHoldResult.Held(),
            LeaseSnapshot = new SudoVdaDriverLeaseSessionSnapshot(1, 10, true, true, "held")
        };
        var dispatcher = new HostAgentDispatcher(executor);

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(
                HostAgentOperation.HoldDisplayLease,
                new HoldDisplayLeasePayload("client-z-fold-7")),
            CancellationToken.None);

        Assert.True(response.Success);
        HoldDisplayLeaseResultPayload payload =
            HostAgentProtocol.ReadPayload<HoldDisplayLeaseResultPayload>(response.Payload);
        Assert.True(payload.Acquired);
        Assert.Equal(1, payload.Lease.LeaseCount);
        Assert.Equal(new[] { "hold:client-z-fold-7", "snapshot" }, executor.Calls);
    }

    [Fact]
    public async Task CreateVirtualDisplayReturnsResolvedWindowsSourceName()
    {
        var executor = new FakeDisplayExecutor
        {
            DisplayName = @"\\.\DISPLAY24"
        };
        var dispatcher = new HostAgentDispatcher(executor);

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(
                HostAgentOperation.CreateVirtualDisplay,
                new CreateVirtualDisplayPayload("client-z-fold-7", 2560, 1600, 120)),
            CancellationToken.None);

        Assert.True(response.Success);
        CreateVirtualDisplayResultPayload payload =
            HostAgentProtocol.ReadPayload<CreateVirtualDisplayResultPayload>(response.Payload);
        Assert.Equal(@"\\.\DISPLAY24", payload.DisplayName);
        Assert.Equal(new[] { "create:client-z-fold-7:2560x1600@120", "resolve:client-z-fold-7" }, executor.Calls);
    }

    [Fact]
    public async Task QueryTopologyReturnsPlatformNeutralPathsAndMappings()
    {
        var executor = new FakeDisplayExecutor
        {
            Topology = DisplayTopologySnapshot.Extended(
                @"\\.\DISPLAY1",
                "client-z-fold-7",
                2560,
                1600,
                120,
                virtualPrimary: false),
            DisplayName = @"\\.\DISPLAY24"
        };
        var dispatcher = new HostAgentDispatcher(executor);

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(HostAgentOperation.QueryTopology, new EmptyHostAgentPayload()),
            CancellationToken.None);

        Assert.True(response.Success);
        DisplayTopologyPayload payload = HostAgentProtocol.ReadPayload<DisplayTopologyPayload>(
            response.Payload);
        Assert.False(payload.IsMirrorMode);
        Assert.Equal(2, payload.Paths.Count);
        DisplayPathPayload virtualPath = Assert.Single(
            payload.Paths,
            path => path.Kind == HostAgentDisplayKind.Virtual);
        Assert.Equal("client-z-fold-7", virtualPath.DisplayId);
        Assert.Equal(2560, virtualPath.Width);
        Assert.Equal(1600, virtualPath.Height);
        Assert.Equal(120, virtualPath.RefreshHz);
        Assert.False(virtualPath.IsPrimary);
        Assert.Equal(@"\\.\DISPLAY24", payload.DisplayNames["client-z-fold-7"]);
    }

    [Theory]
    [InlineData(HostAgentOperation.ReleaseDisplayLease, "release:client-z-fold-7")]
    [InlineData(HostAgentOperation.SetVirtualPrimary, "primary:client-z-fold-7")]
    [InlineData(HostAgentOperation.RemoveVirtualDisplay, "remove:client-z-fold-7")]
    [InlineData(HostAgentOperation.QueryHdrCapability, "hdr:client-z-fold-7")]
    public async Task DisplayIdOperationsRouteOnlyTheRequestedPrimitive(
        HostAgentOperation operation,
        string expectedCall)
    {
        var executor = new FakeDisplayExecutor();
        var dispatcher = new HostAgentDispatcher(executor);

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(operation, new DisplayIdPayload("client-z-fold-7")),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Contains(expectedCall, executor.Calls);
        Assert.DoesNotContain(executor.Calls, call =>
            call.StartsWith("remove:", StringComparison.Ordinal) && expectedCall != call);
        Assert.DoesNotContain("restore", executor.Calls);
    }

    [Fact]
    public async Task RestoreRoutesOnlyPhysicalPrimaryPrimitive()
    {
        var executor = new FakeDisplayExecutor();
        var dispatcher = new HostAgentDispatcher(executor);

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(HostAgentOperation.RestorePhysicalPrimary, new EmptyHostAgentPayload()),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(new[] { "restore" }, executor.Calls);
    }

    [Fact]
    public async Task MalformedPayloadIsRejectedWithoutExecutorUse()
    {
        var executor = new FakeDisplayExecutor();
        var dispatcher = new HostAgentDispatcher(executor);
        HostAgentRequest request = new(
            HostAgentProtocol.CurrentVersion,
            Guid.NewGuid(),
            HostAgentOperation.CreateVirtualDisplay,
            HostAgentProtocol.CreatePayload(new { displayId = "client", width = 2560 }));

        HostAgentResponse response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("invalid-payload", response.ResultCode);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task DriverUpdateStartReturnsDurableAcceptedTransaction()
    {
        var displays = new FakeDisplayExecutor();
        var updates = new FakeDriverUpdateExecutor();
        var dispatcher = new HostAgentDispatcher(displays, updates);
        Guid transactionId = Guid.NewGuid();

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(
                HostAgentOperation.InstallStagedSudoVdaPackage,
                new InstallStagedSudoVdaPackagePayload(
                    "sudovda-22.48.58.193",
                    transactionId,
                    ReportedActiveLeaseCount: 0)),
            new CancellationToken(canceled: true));

        Assert.True(response.Success);
        SudoVdaUpdatePayload payload = HostAgentProtocol.ReadPayload<SudoVdaUpdatePayload>(
            response.Payload);
        Assert.Equal(transactionId, payload.TransactionId);
        Assert.Equal(SudoVdaUpdateState.Accepted, payload.State);
        Assert.Equal(
            new[] { $"start:sudovda-22.48.58.193:{transactionId:D}:0" },
            updates.Calls);
        Assert.Empty(displays.Calls);
    }

    [Fact]
    public async Task DriverUpdateQueryReturnsDurableState()
    {
        var updates = new FakeDriverUpdateExecutor();
        var dispatcher = new HostAgentDispatcher(new FakeDisplayExecutor(), updates);
        Guid transactionId = Guid.NewGuid();
        updates.QueryResult = new SudoVdaUpdatePayload(
            transactionId,
            "sudovda-22.48.58.193",
            SudoVdaUpdateState.Succeeded,
            "driver-update-succeeded",
            PreviousEvidence: null,
            ActiveEvidence: null);

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(
                HostAgentOperation.QuerySudoVdaUpdate,
                new QuerySudoVdaUpdatePayload(transactionId)),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(
            SudoVdaUpdateState.Succeeded,
            HostAgentProtocol.ReadPayload<SudoVdaUpdatePayload>(response.Payload).State);
        Assert.Equal(new[] { $"query:{transactionId:D}" }, updates.Calls);
    }

    [Fact]
    public async Task UnknownDriverUpdateQueryReturnsStableFailure()
    {
        var updates = new FakeDriverUpdateExecutor();
        var dispatcher = new HostAgentDispatcher(new FakeDisplayExecutor(), updates);

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(
                HostAgentOperation.QuerySudoVdaUpdate,
                new QuerySudoVdaUpdatePayload(Guid.NewGuid())),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("driver-update-not-found", response.ResultCode);
    }

    [Fact]
    public async Task DriverUpdateStartFailureUsesDomainResultCode()
    {
        var updates = new FakeDriverUpdateExecutor
        {
            StartError = new SudoVdaUpdateStartException(
                "driver-update-busy",
                "Another driver update transaction is active.")
        };
        var dispatcher = new HostAgentDispatcher(new FakeDisplayExecutor(), updates);

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(
                HostAgentOperation.InstallStagedSudoVdaPackage,
                new InstallStagedSudoVdaPackagePayload(
                    "sudovda-next",
                    Guid.NewGuid(),
                    ReportedActiveLeaseCount: 0)),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("driver-update-busy", response.ResultCode);
        Assert.Equal("Another driver update transaction is active.", response.Diagnostic);
    }

    [Fact]
    public async Task HostAgentUpdateStartReturnsStagedTransaction()
    {
        var updates = new FakeHostAgentUpdateExecutor();
        var dispatcher = new HostAgentDispatcher(
            new FakeDisplayExecutor(),
            hostUpdates: updates);
        Guid transactionId = Guid.NewGuid();
        updates.StageResult = new HostAgentUpdatePayload(
            transactionId,
            "agent-0123456789abcdef",
            HostAgentUpdateState.Staged,
            "host-agent-update-staged",
            new string('A', 40),
            "agent-current",
            ActiveVersionId: null);

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(
                HostAgentOperation.InstallStagedHostAgentPackage,
                new InstallStagedHostAgentPackagePayload(
                    "agent-0123456789abcdef",
                    transactionId)),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(
            HostAgentUpdateState.Staged,
            HostAgentProtocol.ReadPayload<HostAgentUpdatePayload>(response.Payload).State);
        Assert.Equal(
            new[] { $"stage:agent-0123456789abcdef:{transactionId:D}" },
            updates.Calls);
    }

    [Fact]
    public async Task HostAgentUpdateQueryReturnsDurableBootstrapState()
    {
        var updates = new FakeHostAgentUpdateExecutor();
        var dispatcher = new HostAgentDispatcher(
            new FakeDisplayExecutor(),
            hostUpdates: updates);
        Guid transactionId = Guid.NewGuid();
        updates.QueryResult = new HostAgentUpdatePayload(
            transactionId,
            "agent-0123456789abcdef",
            HostAgentUpdateState.Succeeded,
            "host-agent-update-succeeded",
            new string('A', 40),
            "agent-current",
            "agent-0123456789abcdef");

        HostAgentResponse response = await dispatcher.DispatchAsync(
            Request(
                HostAgentOperation.QueryHostAgentUpdate,
                new QueryHostAgentUpdatePayload(transactionId)),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(
            HostAgentUpdateState.Succeeded,
            HostAgentProtocol.ReadPayload<HostAgentUpdatePayload>(response.Payload).State);
        Assert.Equal(new[] { $"query:{transactionId:D}" }, updates.Calls);
    }

    private static HostAgentRequest Request<T>(HostAgentOperation operation, T payload) =>
        new(
            HostAgentProtocol.CurrentVersion,
            Guid.NewGuid(),
            operation,
            HostAgentProtocol.CreatePayload(payload));

    private sealed class FakeDisplayExecutor : IHostAgentDisplayExecutor
    {
        public List<string> Calls { get; } = [];

        public DisplayDriverStatus DriverStatus { get; init; } =
            new(true, "driver-ready");

        public SudoVdaDriverLeaseSessionSnapshot LeaseSnapshot { get; init; } =
            new(0, null, false, true, "idle");

        public SudoVdaDriverLeaseHoldResult HoldResult { get; init; } =
            SudoVdaDriverLeaseHoldResult.Held();

        public DisplayTopologySnapshot Topology { get; init; } =
            DisplayTopologySnapshot.PhysicalOnly(@"\\.\DISPLAY1", 2560, 1600, 240);

        public string? DisplayName { get; init; }

        public DisplayDriverStatus GetDriverStatus()
        {
            Calls.Add("status");
            return DriverStatus;
        }

        public SudoVdaDriverLeaseSessionSnapshot GetLeaseSnapshot()
        {
            Calls.Add("snapshot");
            return LeaseSnapshot;
        }

        public Task<SudoVdaDriverLeaseHoldResult> HoldAsync(
            string displayId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"hold:{displayId}");
            return Task.FromResult(HoldResult);
        }

        public Task ReleaseAsync(string displayId, CancellationToken cancellationToken)
        {
            Calls.Add($"release:{displayId}");
            return Task.CompletedTask;
        }

        public Task<DisplayApiResult> CreateAsync(
            CreateVirtualDisplayPayload request,
            CancellationToken cancellationToken)
        {
            Calls.Add($"create:{request.DisplayId}:{request.Width}x{request.Height}@{request.RefreshHz}");
            return Task.FromResult(DisplayApiResult.Ok());
        }

        public Task<DisplayTopologySnapshot> QueryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Topology);

        public Task<DisplayApiResult> SetVirtualPrimaryAsync(
            string displayId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"primary:{displayId}");
            return Task.FromResult(DisplayApiResult.Ok());
        }

        public Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken)
        {
            Calls.Add("restore");
            return Task.FromResult(DisplayApiResult.Ok());
        }

        public Task<DisplayApiResult> RemoveAsync(
            string displayId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"remove:{displayId}");
            return Task.FromResult(DisplayApiResult.Ok());
        }

        public Task<DisplayHdrCapability> QueryHdrAsync(
            string displayId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"hdr:{displayId}");
            return Task.FromResult(new DisplayHdrCapability(false, false, "SDR"));
        }

        public bool TryResolveDisplayName(string displayId, out string? displayName)
        {
            Calls.Add($"resolve:{displayId}");
            displayName = DisplayName;
            return displayName is not null;
        }
    }

    private sealed class FakeDriverUpdateExecutor : IHostAgentDriverUpdateExecutor
    {
        public List<string> Calls { get; } = [];

        public SudoVdaUpdateStartException? StartError { get; init; }

        public SudoVdaUpdatePayload? QueryResult { get; set; }

        public SudoVdaUpdatePayload Start(
            string packageId,
            Guid transactionId,
            int reportedActiveLeaseCount)
        {
            Calls.Add($"start:{packageId}:{transactionId:D}:{reportedActiveLeaseCount}");
            if (StartError is not null)
            {
                throw StartError;
            }
            return new SudoVdaUpdatePayload(
                transactionId,
                packageId,
                SudoVdaUpdateState.Accepted,
                "driver-update-accepted",
                PreviousEvidence: null,
                ActiveEvidence: null);
        }

        public bool TryGet(Guid transactionId, out SudoVdaUpdatePayload? value)
        {
            Calls.Add($"query:{transactionId:D}");
            value = QueryResult;
            return value is not null;
        }
    }

    private sealed class FakeHostAgentUpdateExecutor : IHostAgentUpdateExecutor
    {
        public List<string> Calls { get; } = [];

        public HostAgentUpdatePayload? StageResult { get; set; }

        public HostAgentUpdatePayload? QueryResult { get; set; }

        public Task<HostAgentUpdatePayload> StageAsync(
            string packageId,
            Guid transactionId,
            CancellationToken cancellationToken)
        {
            Calls.Add($"stage:{packageId}:{transactionId:D}");
            return Task.FromResult(
                StageResult
                ?? throw new InvalidOperationException("Stage result is not configured."));
        }

        public bool TryGet(Guid transactionId, out HostAgentUpdatePayload? value)
        {
            Calls.Add($"query:{transactionId:D}");
            value = QueryResult;
            return value is not null;
        }
    }
}
