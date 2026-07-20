using Beacon.HostAgent.Contracts;
using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.HostAgent;

public sealed class HostAgentWindowsDisplayApi :
    IWindowsDisplayApi,
    IWindowsDisplayLeaseSession,
    IWindowsDisplayNameResolver
{
    private readonly Lock gate = new();
    private readonly IHostAgentConnection connection;
    private readonly Dictionary<string, string> displayNames = new(StringComparer.Ordinal);
    private long observedConnectionRevision = -1;
    private SudoVdaDriverLeaseSessionSnapshot leaseSnapshot = UnavailableLease(
        "Beacon Host Agent connection has not started.");

    public HostAgentWindowsDisplayApi(IHostAgentConnection connection)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public SudoVdaDriverLeaseSessionSnapshot Snapshot
    {
        get
        {
            SynchronizeConnectionState();
            lock (gate)
            {
                return leaseSnapshot;
            }
        }
    }

    public DisplayDriverStatus GetDriverStatus()
    {
        HostAgentConnectionState state = SynchronizeConnectionState();
        return state.Connected && state.Status is not null
            ? new DisplayDriverStatus(
                state.Status.DriverReady,
                state.Status.DriverDiagnostic)
            : new DisplayDriverStatus(Ready: false, state.Diagnostic);
    }

    public async Task<SudoVdaDriverLeaseHoldResult> HoldAsync(
        string displayId,
        CancellationToken cancellationToken)
    {
        HostAgentResponse response = await connection.SendAsync(
            HostAgentOperation.HoldDisplayLease,
            new HoldDisplayLeasePayload(displayId),
            cancellationToken).ConfigureAwait(false);
        if (!response.Success)
        {
            return SudoVdaDriverLeaseHoldResult.Fail(FailureDiagnostic(response));
        }

        HoldDisplayLeaseResultPayload result =
            HostAgentProtocol.ReadPayload<HoldDisplayLeaseResultPayload>(response.Payload);
        RememberLease(result.Lease);
        return result.Acquired
            ? SudoVdaDriverLeaseHoldResult.Held()
            : SudoVdaDriverLeaseHoldResult.AlreadyHeld();
    }

    public async Task ReleaseAsync(string displayId, CancellationToken cancellationToken)
    {
        HostAgentResponse response = await connection.SendAsync(
            HostAgentOperation.ReleaseDisplayLease,
            new DisplayIdPayload(displayId),
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, HostAgentOperation.ReleaseDisplayLease);
        RememberLease(
            HostAgentProtocol.ReadPayload<HostAgentLeaseSnapshotPayload>(response.Payload));
    }

    public async Task<DisplayApiResult> CreateVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        CancellationToken cancellationToken)
    {
        HostAgentResponse response = await connection.SendAsync(
            HostAgentOperation.CreateVirtualDisplay,
            new CreateVirtualDisplayPayload(displayId, width, height, refreshHz),
            cancellationToken).ConfigureAwait(false);
        if (!response.Success)
        {
            return DisplayApiResult.Fail(FailureDiagnostic(response));
        }

        CreateVirtualDisplayResultPayload result =
            HostAgentProtocol.ReadPayload<CreateVirtualDisplayResultPayload>(response.Payload);
        lock (gate)
        {
            displayNames[displayId] = result.DisplayName;
        }
        return DisplayApiResult.Ok();
    }

    public async Task<DisplayTopologySnapshot> QueryTopologyAsync(
        CancellationToken cancellationToken)
    {
        HostAgentResponse response = await connection.SendAsync(
            HostAgentOperation.QueryTopology,
            new EmptyHostAgentPayload(),
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, HostAgentOperation.QueryTopology);
        DisplayTopologyPayload result =
            HostAgentProtocol.ReadPayload<DisplayTopologyPayload>(response.Payload);
        lock (gate)
        {
            displayNames.Clear();
            foreach (KeyValuePair<string, string> mapping in result.DisplayNames)
            {
                displayNames[mapping.Key] = mapping.Value;
            }
        }

        return new DisplayTopologySnapshot(
            result.Paths.Select(ToSnapshot).ToArray(),
            result.IsMirrorMode);
    }

    public Task<DisplayApiResult> SetVirtualPrimaryAsync(
        string displayId,
        CancellationToken cancellationToken) =>
        SendDisplayCommandAsync(
            HostAgentOperation.SetVirtualPrimary,
            new DisplayIdPayload(displayId),
            cancellationToken);

    public Task<DisplayApiResult> RestorePhysicalPrimaryAsync(
        CancellationToken cancellationToken) =>
        SendDisplayCommandAsync(
            HostAgentOperation.RestorePhysicalPrimary,
            new EmptyHostAgentPayload(),
            cancellationToken);

    public async Task<DisplayApiResult> RemoveVirtualDisplayAsync(
        string displayId,
        CancellationToken cancellationToken)
    {
        DisplayApiResult result = await SendDisplayCommandAsync(
            HostAgentOperation.RemoveVirtualDisplay,
            new DisplayIdPayload(displayId),
            cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            lock (gate)
            {
                displayNames.Remove(displayId);
            }
        }
        return result;
    }

    public async Task<DisplayHdrCapability> QueryHdrCapabilityAsync(
        string displayId,
        CancellationToken cancellationToken)
    {
        HostAgentResponse response = await connection.SendAsync(
            HostAgentOperation.QueryHdrCapability,
            new DisplayIdPayload(displayId),
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, HostAgentOperation.QueryHdrCapability);
        DisplayHdrCapabilityPayload result =
            HostAgentProtocol.ReadPayload<DisplayHdrCapabilityPayload>(response.Payload);
        return new DisplayHdrCapability(result.Supported, result.Enabled, result.Reason);
    }

    public bool TryResolveDisplayName(string displayId, out string? displayName)
    {
        lock (gate)
        {
            return displayNames.TryGetValue(displayId, out displayName);
        }
    }

    private async Task<DisplayApiResult> SendDisplayCommandAsync<TPayload>(
        HostAgentOperation operation,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        HostAgentResponse response = await connection.SendAsync(
            operation,
            payload,
            cancellationToken).ConfigureAwait(false);
        return response.Success
            ? DisplayApiResult.Ok()
            : DisplayApiResult.Fail(FailureDiagnostic(response));
    }

    private HostAgentConnectionState SynchronizeConnectionState()
    {
        HostAgentConnectionState current = connection.State;
        lock (gate)
        {
            if (current.Revision == observedConnectionRevision)
            {
                return current;
            }

            observedConnectionRevision = current.Revision;
            leaseSnapshot = current.Connected && current.Status is not null
                ? ToSnapshot(current.Status.Lease)
                : UnavailableLease(current.Diagnostic);
            if (!current.Connected)
            {
                displayNames.Clear();
            }
        }
        return current;
    }

    private void RememberLease(HostAgentLeaseSnapshotPayload lease)
    {
        HostAgentConnectionState current = connection.State;
        lock (gate)
        {
            observedConnectionRevision = current.Revision;
            leaseSnapshot = ToSnapshot(lease);
        }
    }

    private static DisplayPathSnapshot ToSnapshot(DisplayPathPayload path) =>
        new(
            path.DisplayId,
            path.Kind == HostAgentDisplayKind.Physical
                ? DisplayPathKind.Physical
                : DisplayPathKind.Virtual,
            path.Width,
            path.Height,
            path.RefreshHz,
            path.IsPrimary,
            path.X,
            path.Y);

    private static SudoVdaDriverLeaseSessionSnapshot ToSnapshot(
        HostAgentLeaseSnapshotPayload lease) =>
        new(
            lease.LeaseCount,
            lease.WatchdogTimeoutSeconds,
            lease.HeartbeatActive,
            lease.Healthy,
            lease.Diagnostic);

    private static SudoVdaDriverLeaseSessionSnapshot UnavailableLease(string diagnostic) =>
        new(
            LeaseCount: 0,
            WatchdogTimeoutSeconds: null,
            HeartbeatActive: false,
            Healthy: false,
            diagnostic);

    private static void EnsureSuccess(
        HostAgentResponse response,
        HostAgentOperation operation)
    {
        if (!response.Success)
        {
            throw new HostAgentOperationException(operation, response.ResultCode, FailureDiagnostic(response));
        }
    }

    private static string FailureDiagnostic(HostAgentResponse response) =>
        string.IsNullOrWhiteSpace(response.Diagnostic)
            ? $"Host Agent operation failed ({response.ResultCode})."
            : response.Diagnostic;
}

public sealed class HostAgentOperationException : InvalidOperationException
{
    public HostAgentOperationException(
        HostAgentOperation operation,
        string resultCode,
        string message)
        : base(message)
    {
        Operation = operation;
        ResultCode = resultCode;
    }

    public HostAgentOperation Operation { get; }

    public string ResultCode { get; }
}
