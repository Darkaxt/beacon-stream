using Beacon.HostAgent.Contracts;
using Beacon.Platform.Windows.Displays;

namespace Beacon.HostAgent;

internal sealed class WindowsHostAgentDisplayExecutor(
    WindowsDisplayApi api,
    WindowsDisplayNameMap displayNames) : IHostAgentDisplayExecutor
{
    public DisplayDriverStatus GetDriverStatus() => api.GetDriverStatus();

    public SudoVdaDriverLeaseSessionSnapshot GetLeaseSnapshot() => api.Snapshot;

    public Task<SudoVdaDriverLeaseHoldResult> HoldAsync(
        string displayId,
        CancellationToken cancellationToken) =>
        api.HoldAsync(displayId, cancellationToken);

    public Task ReleaseAsync(string displayId, CancellationToken cancellationToken) =>
        api.ReleaseAsync(displayId, cancellationToken);

    public Task<DisplayApiResult> CreateAsync(
        CreateVirtualDisplayPayload request,
        CancellationToken cancellationToken) =>
        api.CreateVirtualDisplayAsync(
            request.DisplayId,
            request.Width,
            request.Height,
            request.RefreshHz,
            cancellationToken);

    public Task<DisplayTopologySnapshot> QueryAsync(CancellationToken cancellationToken) =>
        api.QueryTopologyAsync(cancellationToken);

    public Task<DisplayApiResult> SetVirtualPrimaryAsync(
        string displayId,
        CancellationToken cancellationToken) =>
        api.SetVirtualPrimaryAsync(displayId, cancellationToken);

    public Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken) =>
        api.RestorePhysicalPrimaryAsync(cancellationToken);

    public Task<DisplayApiResult> RemoveAsync(
        string displayId,
        CancellationToken cancellationToken) =>
        api.RemoveVirtualDisplayAsync(displayId, cancellationToken);

    public Task<DisplayHdrCapability> QueryHdrAsync(
        string displayId,
        CancellationToken cancellationToken) =>
        api.QueryHdrCapabilityAsync(displayId, cancellationToken);

    public Task<DisplayApiResult> SetHdrStateAsync(
        string displayId,
        bool enabled,
        CancellationToken cancellationToken) =>
        api.SetHdrStateAsync(displayId, enabled, cancellationToken);

    public bool TryResolveDisplayName(string displayId, out string? displayName) =>
        displayNames.TryResolveDisplayName(displayId, out displayName);
}
