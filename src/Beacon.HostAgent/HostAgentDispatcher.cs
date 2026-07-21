using System.Text.Json;
using Beacon.HostAgent.Contracts;
using Beacon.HostAgent.DriverUpdates;
using Beacon.HostAgent.HostUpdates;
using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent;

internal sealed class HostAgentDispatcher(
    IHostAgentDisplayExecutor displays,
    IHostAgentDriverUpdateExecutor? driverUpdates = null,
    IHostAgentUpdateExecutor? hostUpdates = null)
{
    private readonly IHostAgentDriverUpdateExecutor driverUpdates =
        driverUpdates ?? UnavailableDriverUpdateExecutor.Instance;
    private readonly IHostAgentUpdateExecutor hostUpdates =
        hostUpdates ?? UnavailableHostAgentUpdateExecutor.Instance;

    public async Task<HostAgentResponse> DispatchAsync(
        HostAgentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProtocolVersion != HostAgentProtocol.CurrentVersion)
        {
            return Failure(request, "protocol-mismatch", "Unsupported Host Agent protocol version.");
        }

        try
        {
            return request.Operation switch
            {
                HostAgentOperation.GetStatus => GetStatus(request),
                HostAgentOperation.HoldDisplayLease => await HoldAsync(request, cancellationToken)
                    .ConfigureAwait(false),
                HostAgentOperation.ReleaseDisplayLease => await ReleaseAsync(request, cancellationToken)
                    .ConfigureAwait(false),
                HostAgentOperation.CreateVirtualDisplay => await CreateAsync(request, cancellationToken)
                    .ConfigureAwait(false),
                HostAgentOperation.QueryTopology => await QueryAsync(request, cancellationToken)
                    .ConfigureAwait(false),
                HostAgentOperation.SetVirtualPrimary => await SetVirtualPrimaryAsync(request, cancellationToken)
                    .ConfigureAwait(false),
                HostAgentOperation.RestorePhysicalPrimary => await RestorePhysicalPrimaryAsync(
                    request,
                    cancellationToken).ConfigureAwait(false),
                HostAgentOperation.RemoveVirtualDisplay => await RemoveAsync(request, cancellationToken)
                    .ConfigureAwait(false),
                HostAgentOperation.QueryHdrCapability => await QueryHdrAsync(request, cancellationToken)
                    .ConfigureAwait(false),
                HostAgentOperation.InstallStagedSudoVdaPackage => StartDriverUpdate(request),
                HostAgentOperation.QuerySudoVdaUpdate => QueryDriverUpdate(request),
                HostAgentOperation.InstallStagedHostAgentPackage => await StartHostAgentUpdateAsync(
                    request,
                    cancellationToken).ConfigureAwait(false),
                HostAgentOperation.QueryHostAgentUpdate => QueryHostAgentUpdate(request),
                _ => Failure(request, "unsupported-operation", "Unsupported Host Agent operation.")
            };
        }
        catch (JsonException)
        {
            return Failure(request, "invalid-payload", "Host Agent request payload is invalid.");
        }
        catch (ArgumentException)
        {
            return Failure(request, "invalid-payload", "Host Agent request payload is invalid.");
        }
        catch (SudoVdaUpdateStartException error)
        {
            return Failure(request, error.Code, error.Message);
        }
        catch (HostAgentUpdateStartException error)
        {
            return Failure(request, error.Code, error.Message);
        }
    }

    public async Task<HostAgentDispatchOutcome> DispatchWithOutcomeAsync(
        HostAgentRequest request,
        CancellationToken cancellationToken)
    {
        HostAgentResponse response = await DispatchAsync(request, cancellationToken)
            .ConfigureAwait(false);
        HostAgentPostResponseAction action = HostAgentPostResponseAction.None;
        if (request.Operation == HostAgentOperation.InstallStagedHostAgentPackage
            && response.Success)
        {
            HostAgentUpdatePayload update =
                HostAgentProtocol.ReadPayload<HostAgentUpdatePayload>(response.Payload);
            if (update.State == HostAgentUpdateState.Staged)
            {
                action = HostAgentPostResponseAction.ApplyUpdate;
            }
        }
        return new HostAgentDispatchOutcome(response, action);
    }

    private HostAgentResponse GetStatus(HostAgentRequest request)
    {
        _ = HostAgentProtocol.ReadPayload<EmptyHostAgentPayload>(request.Payload);
        DisplayDriverStatus driver = displays.GetDriverStatus();
        SudoVdaDriverLeaseSessionSnapshot lease = displays.GetLeaseSnapshot();
        return Success(
            request,
            new HostAgentStatusPayload(
                driver.Ready,
                driver.Diagnostic,
                ToPayload(lease)));
    }

    private async Task<HostAgentResponse> HoldAsync(
        HostAgentRequest request,
        CancellationToken cancellationToken)
    {
        HoldDisplayLeasePayload payload = HostAgentProtocol.ReadPayload<HoldDisplayLeasePayload>(
            request.Payload);
        SudoVdaDriverLeaseHoldResult result = await displays.HoldAsync(
            RequireDisplayId(payload.DisplayId),
            cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return Failure(
                request,
                "display-lease-hold-failed",
                result.Error ?? "SudoVDA display lease hold failed.");
        }

        return Success(
            request,
            new HoldDisplayLeaseResultPayload(
                result.Acquired,
                ToPayload(displays.GetLeaseSnapshot())));
    }

    private async Task<HostAgentResponse> ReleaseAsync(
        HostAgentRequest request,
        CancellationToken cancellationToken)
    {
        DisplayIdPayload payload = HostAgentProtocol.ReadPayload<DisplayIdPayload>(request.Payload);
        await displays.ReleaseAsync(RequireDisplayId(payload.DisplayId), cancellationToken)
            .ConfigureAwait(false);
        return Success(request, ToPayload(displays.GetLeaseSnapshot()));
    }

    private async Task<HostAgentResponse> CreateAsync(
        HostAgentRequest request,
        CancellationToken cancellationToken)
    {
        CreateVirtualDisplayPayload payload =
            HostAgentProtocol.ReadPayload<CreateVirtualDisplayPayload>(request.Payload);
        ValidateCreatePayload(payload);
        DisplayApiResult result = await displays.CreateAsync(payload, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            return DisplayFailure(request, result);
        }

        if (!displays.TryResolveDisplayName(payload.DisplayId, out string? displayName)
            || string.IsNullOrWhiteSpace(displayName))
        {
            return Failure(
                request,
                "display-name-unavailable",
                "Created virtual display has no verified Windows source name.");
        }

        return Success(request, new CreateVirtualDisplayResultPayload(displayName));
    }

    private async Task<HostAgentResponse> QueryAsync(
        HostAgentRequest request,
        CancellationToken cancellationToken)
    {
        _ = HostAgentProtocol.ReadPayload<EmptyHostAgentPayload>(request.Payload);
        DisplayTopologySnapshot topology = await displays.QueryAsync(cancellationToken)
            .ConfigureAwait(false);
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DisplayPathSnapshot path in topology.Paths)
        {
            if (displays.TryResolveDisplayName(path.DisplayId, out string? displayName)
                && !string.IsNullOrWhiteSpace(displayName))
            {
                mappings[path.DisplayId] = displayName;
            }
        }

        return Success(
            request,
            new DisplayTopologyPayload(
                topology.Paths.Select(ToPayload).ToArray(),
                topology.IsMirrorMode,
                mappings));
    }

    private async Task<HostAgentResponse> SetVirtualPrimaryAsync(
        HostAgentRequest request,
        CancellationToken cancellationToken)
    {
        DisplayIdPayload payload = HostAgentProtocol.ReadPayload<DisplayIdPayload>(request.Payload);
        DisplayApiResult result = await displays.SetVirtualPrimaryAsync(
            RequireDisplayId(payload.DisplayId),
            cancellationToken).ConfigureAwait(false);
        return result.Success ? Success(request) : DisplayFailure(request, result);
    }

    private async Task<HostAgentResponse> RestorePhysicalPrimaryAsync(
        HostAgentRequest request,
        CancellationToken cancellationToken)
    {
        _ = HostAgentProtocol.ReadPayload<EmptyHostAgentPayload>(request.Payload);
        DisplayApiResult result = await displays.RestorePhysicalPrimaryAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.Success ? Success(request) : DisplayFailure(request, result);
    }

    private async Task<HostAgentResponse> RemoveAsync(
        HostAgentRequest request,
        CancellationToken cancellationToken)
    {
        DisplayIdPayload payload = HostAgentProtocol.ReadPayload<DisplayIdPayload>(request.Payload);
        DisplayApiResult result = await displays.RemoveAsync(
            RequireDisplayId(payload.DisplayId),
            cancellationToken).ConfigureAwait(false);
        return result.Success ? Success(request) : DisplayFailure(request, result);
    }

    private async Task<HostAgentResponse> QueryHdrAsync(
        HostAgentRequest request,
        CancellationToken cancellationToken)
    {
        DisplayIdPayload payload = HostAgentProtocol.ReadPayload<DisplayIdPayload>(request.Payload);
        DisplayHdrCapability result = await displays.QueryHdrAsync(
            RequireDisplayId(payload.DisplayId),
            cancellationToken).ConfigureAwait(false);
        return Success(
            request,
            new DisplayHdrCapabilityPayload(result.Supported, result.Enabled, result.Reason));
    }

    private HostAgentResponse StartDriverUpdate(HostAgentRequest request)
    {
        InstallStagedSudoVdaPackagePayload payload =
            HostAgentProtocol.ReadPayload<InstallStagedSudoVdaPackagePayload>(request.Payload);
        SudoVdaUpdatePayload result = driverUpdates.Start(
            payload.PackageId,
            payload.TransactionId,
            payload.ReportedActiveLeaseCount);
        return Success(request, result);
    }

    private HostAgentResponse QueryDriverUpdate(HostAgentRequest request)
    {
        QuerySudoVdaUpdatePayload payload =
            HostAgentProtocol.ReadPayload<QuerySudoVdaUpdatePayload>(request.Payload);
        if (payload.TransactionId == Guid.Empty)
        {
            throw new ArgumentException("Driver update transaction id is required.");
        }
        return driverUpdates.TryGet(payload.TransactionId, out SudoVdaUpdatePayload? result)
            && result is not null
                ? Success(request, result)
                : Failure(
                    request,
                    "driver-update-not-found",
                    "Driver update transaction was not found.");
    }

    private async Task<HostAgentResponse> StartHostAgentUpdateAsync(
        HostAgentRequest request,
        CancellationToken cancellationToken)
    {
        InstallStagedHostAgentPackagePayload payload =
            HostAgentProtocol.ReadPayload<InstallStagedHostAgentPackagePayload>(request.Payload);
        HostAgentUpdatePayload result = await hostUpdates.StageAsync(
            payload.PackageId,
            payload.TransactionId,
            cancellationToken).ConfigureAwait(false);
        return Success(request, result);
    }

    private HostAgentResponse QueryHostAgentUpdate(HostAgentRequest request)
    {
        QueryHostAgentUpdatePayload payload =
            HostAgentProtocol.ReadPayload<QueryHostAgentUpdatePayload>(request.Payload);
        if (payload.TransactionId == Guid.Empty)
        {
            throw new ArgumentException("Host Agent update transaction id is required.");
        }
        return hostUpdates.TryGet(payload.TransactionId, out HostAgentUpdatePayload? result)
            && result is not null
                ? Success(request, result)
                : Failure(
                    request,
                    "host-agent-update-not-found",
                    "Host Agent update transaction was not found.");
    }

    private static HostAgentLeaseSnapshotPayload ToPayload(
        SudoVdaDriverLeaseSessionSnapshot snapshot) =>
        new(
            snapshot.LeaseCount,
            snapshot.WatchdogTimeoutSeconds,
            snapshot.HeartbeatActive,
            snapshot.Healthy,
            snapshot.Diagnostic);

    private static DisplayPathPayload ToPayload(DisplayPathSnapshot path) =>
        new(
            path.DisplayId,
            path.Kind == DisplayPathKind.Physical
                ? HostAgentDisplayKind.Physical
                : HostAgentDisplayKind.Virtual,
            path.Width,
            path.Height,
            path.RefreshHz,
            path.IsPrimary,
            path.X,
            path.Y);

    private static string RequireDisplayId(string displayId) =>
        string.IsNullOrWhiteSpace(displayId)
            ? throw new ArgumentException("Display id is required.", nameof(displayId))
            : displayId.Trim();

    private static void ValidateCreatePayload(CreateVirtualDisplayPayload payload)
    {
        _ = RequireDisplayId(payload.DisplayId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payload.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payload.Height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payload.RefreshHz);
    }

    private static HostAgentResponse DisplayFailure(
        HostAgentRequest request,
        DisplayApiResult result) =>
        Failure(
            request,
            "display-operation-failed",
            result.Error ?? "Windows display operation failed.");

    private static HostAgentResponse Success<T>(HostAgentRequest request, T payload) =>
        new(
            HostAgentProtocol.CurrentVersion,
            request.RequestId,
            Success: true,
            ResultCode: "ok",
            Diagnostic: string.Empty,
            HostAgentProtocol.CreatePayload(payload));

    private static HostAgentResponse Success(HostAgentRequest request) =>
        Success(request, new EmptyHostAgentPayload());

    private static HostAgentResponse Failure(
        HostAgentRequest request,
        string resultCode,
        string diagnostic) =>
        new(
            HostAgentProtocol.CurrentVersion,
            request.RequestId,
            Success: false,
            resultCode,
            diagnostic,
            HostAgentProtocol.CreatePayload(new EmptyHostAgentPayload()));

    private sealed class UnavailableDriverUpdateExecutor : IHostAgentDriverUpdateExecutor
    {
        public static UnavailableDriverUpdateExecutor Instance { get; } = new();

        public SudoVdaUpdatePayload Start(
            string packageId,
            Guid transactionId,
            int reportedActiveLeaseCount) =>
            throw new SudoVdaUpdateStartException(
                "driver-update-unavailable",
                "Host Agent driver updates are unavailable.");

        public bool TryGet(Guid transactionId, out SudoVdaUpdatePayload? value)
        {
            value = null;
            return false;
        }
    }

    private sealed class UnavailableHostAgentUpdateExecutor : IHostAgentUpdateExecutor
    {
        public static UnavailableHostAgentUpdateExecutor Instance { get; } = new();

        public Task<HostAgentUpdatePayload> StageAsync(
            string packageId,
            Guid transactionId,
            CancellationToken cancellationToken) =>
            throw new HostAgentUpdateStartException(
                "host-agent-update-unavailable",
                "Host Agent self-update is unavailable.");

        public bool TryGet(Guid transactionId, out HostAgentUpdatePayload? value)
        {
            value = null;
            return false;
        }
    }
}
