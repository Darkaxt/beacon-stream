using Beacon.Core.Displays;

namespace Beacon.Platform.Windows.Displays;

public sealed class WindowsDisplayBackend(IWindowsDisplayApi api) : IDisplayBackend
{
    public async Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(
        string displayId,
        int width,
        int height,
        int refreshHz,
        HdrPreference hdrPreference,
        CancellationToken cancellationToken)
    {
        DisplayDriverStatus driverStatus = api.GetDriverStatus();
        if (!driverStatus.Ready)
        {
            return DisplayEnsureResult.Fail(driverStatus.Diagnostic);
        }

        DisplayApiResult createResult = await api.CreateVirtualDisplayAsync(
            displayId,
            width,
            height,
            refreshHz,
            cancellationToken);
        if (!createResult.Success)
        {
            return DisplayEnsureResult.Fail(createResult.Error ?? $"Unable to create virtual display {displayId}.");
        }

        DisplayTopologySnapshot afterCreate = await api.QueryTopologyAsync(cancellationToken);
        if (afterCreate.IsMirrorMode)
        {
            return DisplayEnsureResult.Fail($"Refusing mirror mode for virtual display {displayId}.");
        }

        if (!afterCreate.HasDisplayMode(displayId, width, height, refreshHz))
        {
            return DisplayEnsureResult.Fail($"Virtual display {displayId} did not expose {width}x{height}@{refreshHz}.");
        }

        DisplayApiResult primaryResult = await api.SetVirtualPrimaryAsync(displayId, cancellationToken);
        if (!primaryResult.Success)
        {
            return DisplayEnsureResult.Fail(primaryResult.Error ?? $"Unable to make virtual display {displayId} primary.");
        }

        DisplayTopologySnapshot afterPrimary = await api.QueryTopologyAsync(cancellationToken);
        if (!afterPrimary.IsPrimary(displayId))
        {
            return DisplayEnsureResult.Fail($"Virtual display {displayId} was not primary after topology apply.");
        }

        DisplayHdrCapability hdrCapability = await api.QueryHdrCapabilityAsync(displayId, cancellationToken);
        return NegotiateHdr(hdrPreference, hdrCapability);
    }

    public async Task RestorePhysicalPrimaryAsync(CancellationToken cancellationToken)
    {
        DisplayApiResult restoreResult = await api.RestorePhysicalPrimaryAsync(cancellationToken);
        if (!restoreResult.Success)
        {
            return;
        }

        await api.QueryTopologyAsync(cancellationToken);
    }

    public async Task RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken)
    {
        await api.RemoveVirtualDisplayAsync(displayId, cancellationToken);
    }

    private static DisplayEnsureResult NegotiateHdr(HdrPreference preference, DisplayHdrCapability capability)
    {
        if (preference == HdrPreference.Off)
        {
            return DisplayEnsureResult.Ok(hdrReason: "HDR disabled by profile.");
        }

        if (capability.Supported && capability.Enabled)
        {
            return DisplayEnsureResult.Ok(hdrEnabled: true, hdrReason: capability.Reason);
        }

        if (preference == HdrPreference.Require)
        {
            return DisplayEnsureResult.Fail($"HDR required but unavailable: {capability.Reason}");
        }

        return DisplayEnsureResult.Ok(hdrReason: capability.Reason);
    }
}
