using Beacon.HostAgent.Contracts;
using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent;

internal interface IHostAgentDisplayExecutor
{
    DisplayDriverStatus GetDriverStatus();

    SudoVdaDriverLeaseSessionSnapshot GetLeaseSnapshot();

    Task<SudoVdaDriverLeaseHoldResult> HoldAsync(
        string displayId,
        CancellationToken cancellationToken);

    Task ReleaseAsync(string displayId, CancellationToken cancellationToken);

    Task<DisplayApiResult> CreateAsync(
        CreateVirtualDisplayPayload request,
        CancellationToken cancellationToken);

    Task<DisplayTopologySnapshot> QueryAsync(CancellationToken cancellationToken);

    Task<DisplayApiResult> SetVirtualPrimaryAsync(
        string displayId,
        CancellationToken cancellationToken);

    Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken);

    Task<DisplayApiResult> RemoveAsync(
        string displayId,
        CancellationToken cancellationToken);

    Task<DisplayHdrCapability> QueryHdrAsync(
        string displayId,
        CancellationToken cancellationToken);

    bool TryResolveDisplayName(string displayId, out string? displayName);
}
