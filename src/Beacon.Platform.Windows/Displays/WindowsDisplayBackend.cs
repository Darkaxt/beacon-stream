using Beacon.Core.Displays;

namespace Beacon.Platform.Windows.Displays;

public sealed class WindowsDisplayBackend(IWindowsDisplayApi api) : IDisplayBackend
{
    private readonly List<DisplayOperationLogEntry> operationLog = [];

    public IReadOnlyList<DisplayOperationLogEntry> OperationLog => operationLog;

    public async Task<DisplayHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        DisplayDriverStatus driverStatus = api.GetDriverStatus();
        try
        {
            DisplayTopologySnapshot topology = await api.QueryTopologyAsync(cancellationToken);
            return new DisplayHealth(
                DriverReady: driverStatus.Ready,
                Diagnostic: driverStatus.Diagnostic,
                TopologyAvailable: true,
                MirrorMode: topology.IsMirrorMode,
                PhysicalPrimaryVerified: topology.PhysicalPrimaryVerified,
                Paths: topology.Paths
                    .Select(path => new DisplayPathHealth(
                        path.DisplayId,
                        path.Kind.ToString(),
                        path.Width,
                        path.Height,
                        path.RefreshHz,
                        path.IsPrimary,
                        path.X,
                        path.Y))
                    .ToArray());
        }
        catch (Exception ex)
        {
            return new DisplayHealth(
                DriverReady: driverStatus.Ready,
                Diagnostic: $"{driverStatus.Diagnostic} Topology query failed: {ex.Message}",
                TopologyAvailable: false,
                MirrorMode: false,
                PhysicalPrimaryVerified: false,
                Paths: []);
        }
    }

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
            DisplayEnsureResult fail = DisplayEnsureResult.Fail(driverStatus.Diagnostic);
            LogEnsure(displayId, width, height, refreshHz, before: null, after: null, fail, "driver-not-ready");
            return fail;
        }

        DisplayTopologySnapshot before = await api.QueryTopologyAsync(cancellationToken);
        DisplayApiResult createResult = await api.CreateVirtualDisplayAsync(
            displayId,
            width,
            height,
            refreshHz,
            cancellationToken);
        if (!createResult.Success)
        {
            DisplayEnsureResult fail = DisplayEnsureResult.Fail(createResult.Error ?? $"Unable to create virtual display {displayId}.");
            LogEnsure(displayId, width, height, refreshHz, before, after: null, fail, "create-failed");
            return fail;
        }

        DisplayTopologySnapshot afterCreate = await api.QueryTopologyAsync(cancellationToken);
        if (afterCreate.IsMirrorMode)
        {
            DisplayEnsureResult fail = await FailAfterCreateAsync(
                displayId,
                $"Refusing mirror mode for virtual display {displayId}.",
                cancellationToken);
            LogEnsure(displayId, width, height, refreshHz, before, afterCreate, fail, "mirror-mode-rejected");
            return fail;
        }

        if (!afterCreate.HasDisplayMode(displayId, width, height, refreshHz))
        {
            DisplayEnsureResult fail = await FailAfterCreateAsync(
                displayId,
                $"Virtual display {displayId} did not expose {width}x{height}@{refreshHz}.",
                cancellationToken);
            LogEnsure(displayId, width, height, refreshHz, before, afterCreate, fail, "mode-verification-failed");
            return fail;
        }

        DisplayApiResult primaryResult = await api.SetVirtualPrimaryAsync(displayId, cancellationToken);
        if (!primaryResult.Success)
        {
            DisplayEnsureResult fail = await FailAfterCreateAsync(
                displayId,
                primaryResult.Error ?? $"Unable to make virtual display {displayId} primary.",
                cancellationToken);
            LogEnsure(displayId, width, height, refreshHz, before, afterCreate, fail, "primary-apply-failed");
            return fail;
        }

        DisplayTopologySnapshot afterPrimary = await api.QueryTopologyAsync(cancellationToken);
        if (!afterPrimary.IsPrimary(displayId))
        {
            DisplayEnsureResult fail = await FailAfterCreateAsync(
                displayId,
                $"Virtual display {displayId} was not primary after topology apply.",
                cancellationToken);
            LogEnsure(displayId, width, height, refreshHz, before, afterPrimary, fail, "primary-verification-failed");
            return fail;
        }

        DisplayHdrCapability hdrCapability = await api.QueryHdrCapabilityAsync(displayId, cancellationToken);
        DisplayEnsureResult result = NegotiateHdr(hdrPreference, hdrCapability);
        LogEnsure(displayId, width, height, refreshHz, before, afterPrimary, result, "virtual-primary topology verified");
        return result;
    }

    public async Task<DisplayEnsureResult> PrepareVirtualDisplayAsync(
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
            DisplayEnsureResult fail = DisplayEnsureResult.Fail(driverStatus.Diagnostic);
            LogPrepare(displayId, width, height, refreshHz, before: null, after: null, fail, "driver-not-ready");
            return fail;
        }

        DisplayTopologySnapshot before = await api.QueryTopologyAsync(cancellationToken);
        DisplayTopologySnapshot preparedTopology = before;
        bool createdDisplay = false;

        if (!before.HasDisplayMode(displayId, width, height, refreshHz))
        {
            DisplayApiResult createResult = await api.CreateVirtualDisplayAsync(
                displayId,
                width,
                height,
                refreshHz,
                cancellationToken);
            if (!createResult.Success)
            {
                DisplayEnsureResult fail = DisplayEnsureResult.Fail(createResult.Error ?? $"Unable to create virtual display {displayId}.");
                LogPrepare(displayId, width, height, refreshHz, before, after: null, fail, "create-failed");
                return fail;
            }

            createdDisplay = true;
            preparedTopology = await api.QueryTopologyAsync(cancellationToken);
        }

        if (preparedTopology.IsMirrorMode)
        {
            DisplayEnsureResult fail = await FailPreparedAsync(
                displayId,
                $"Refusing mirror mode for virtual display {displayId}.",
                createdDisplay,
                cancellationToken);
            LogPrepare(displayId, width, height, refreshHz, before, preparedTopology, fail, "mirror-mode-rejected");
            return fail;
        }

        if (!preparedTopology.HasDisplayMode(displayId, width, height, refreshHz))
        {
            DisplayEnsureResult fail = await FailPreparedAsync(
                displayId,
                $"Virtual display {displayId} did not expose {width}x{height}@{refreshHz}.",
                createdDisplay,
                cancellationToken);
            LogPrepare(displayId, width, height, refreshHz, before, preparedTopology, fail, "mode-verification-failed");
            return fail;
        }

        if (preparedTopology.IsPrimary(displayId) || !preparedTopology.PhysicalPrimaryVerified)
        {
            DisplayRestoreResult restoreResult = await RestorePhysicalPrimaryAsync(cancellationToken);
            if (!restoreResult.Success)
            {
                DisplayEnsureResult fail = await FailPreparedAsync(
                    displayId,
                    $"Physical primary restore failed after preparing virtual display {displayId}: {restoreResult.Error}",
                    createdDisplay,
                    cancellationToken);
                LogPrepare(displayId, width, height, refreshHz, before, preparedTopology, fail, "physical-primary-restore-failed");
                return fail;
            }

            preparedTopology = await api.QueryTopologyAsync(cancellationToken);
            if (preparedTopology.IsMirrorMode)
            {
                DisplayEnsureResult fail = await FailPreparedAsync(
                    displayId,
                    $"Refusing mirror mode for virtual display {displayId} after physical primary restore.",
                    createdDisplay,
                    cancellationToken);
                LogPrepare(displayId, width, height, refreshHz, before, preparedTopology, fail, "restore-left-mirror-mode");
                return fail;
            }

            if (!preparedTopology.HasDisplayMode(displayId, width, height, refreshHz))
            {
                DisplayEnsureResult fail = await FailPreparedAsync(
                    displayId,
                    $"Virtual display {displayId} no longer exposed {width}x{height}@{refreshHz} after physical primary restore.",
                    createdDisplay,
                    cancellationToken);
                LogPrepare(displayId, width, height, refreshHz, before, preparedTopology, fail, "restore-lost-mode");
                return fail;
            }

            if (preparedTopology.IsPrimary(displayId) || !preparedTopology.PhysicalPrimaryVerified)
            {
                DisplayEnsureResult fail = await FailPreparedAsync(
                    displayId,
                    $"Virtual display {displayId} was prepared but the physical display was not verified as primary.",
                    createdDisplay,
                    cancellationToken);
                LogPrepare(displayId, width, height, refreshHz, before, preparedTopology, fail, "physical-primary-not-verified");
                return fail;
            }
        }

        DisplayHdrCapability hdrCapability = await api.QueryHdrCapabilityAsync(displayId, cancellationToken);
        DisplayEnsureResult result = NegotiateHdr(hdrPreference, hdrCapability);
        LogPrepare(displayId, width, height, refreshHz, before, preparedTopology, result, "prepared extended topology verified");
        return result;
    }

    public async Task<DisplayRestoreResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken)
    {
        var seenUnverifiedTopologies = new HashSet<string>(StringComparer.Ordinal);
        DisplayTopologySnapshot before = await api.QueryTopologyAsync(cancellationToken);

        while (true)
        {
            DisplayApiResult restoreResult = await api.RestorePhysicalPrimaryAsync(cancellationToken);
            if (!restoreResult.Success)
            {
                DisplayRestoreResult fail = DisplayRestoreResult.Fail(restoreResult.Error ?? "Physical primary restore failed.");
                LogRestore(before, after: null, fail, "restore-apply-failed");
                return fail;
            }

            DisplayTopologySnapshot topology = await api.QueryTopologyAsync(cancellationToken);
            if (topology.PhysicalPrimaryVerified)
            {
                DisplayRestoreResult result = DisplayRestoreResult.Ok();
                LogRestore(before, topology, result, "physical primary verified");
                return result;
            }

            if (!seenUnverifiedTopologies.Add(topology.Fingerprint))
            {
                DisplayRestoreResult fail = DisplayRestoreResult.Fail(
                    $"Physical primary restore was not verified after topology reconciliation. LastTopology={topology.Fingerprint}.");
                LogRestore(before, topology, fail, "restore-verification-failed");
                return fail;
            }
        }
    }

    public async Task<DisplayRemoveResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken)
    {
        DisplayApiResult result = await api.RemoveVirtualDisplayAsync(displayId, cancellationToken);
        return result.Success
            ? DisplayRemoveResult.Ok()
            : DisplayRemoveResult.Fail(result.Error ?? $"Virtual display {displayId} removal failed.");
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

    private async Task<DisplayEnsureResult> FailAfterCreateAsync(
        string displayId,
        string error,
        CancellationToken cancellationToken)
    {
        DisplayApiResult removeResult = await api.RemoveVirtualDisplayAsync(displayId, cancellationToken);
        if (!removeResult.Success)
        {
            return DisplayEnsureResult.Fail($"{error} Cleanup failed: {removeResult.Error}");
        }

        return DisplayEnsureResult.Fail(error);
    }

    private async Task<DisplayEnsureResult> FailPreparedAsync(
        string displayId,
        string error,
        bool removeCreatedDisplay,
        CancellationToken cancellationToken) =>
        removeCreatedDisplay
            ? await FailAfterCreateAsync(displayId, error, cancellationToken)
            : DisplayEnsureResult.Fail(error);

    private void LogEnsure(
        string displayId,
        int width,
        int height,
        int refreshHz,
        DisplayTopologySnapshot? before,
        DisplayTopologySnapshot? after,
        DisplayEnsureResult result,
        string reason)
    {
        operationLog.Add(new DisplayOperationLogEntry(
            Operation: "ensure-virtual-display",
            DisplayId: displayId,
            Width: width,
            Height: height,
            RefreshHz: refreshHz,
            Primary: after?.IsPrimary(displayId),
            HdrEnabled: result.HdrEnabled,
            Reason: result.Success ? reason : $"{reason}: {result.Error}",
            Before: before,
            After: after));
    }

    private void LogPrepare(
        string displayId,
        int width,
        int height,
        int refreshHz,
        DisplayTopologySnapshot? before,
        DisplayTopologySnapshot? after,
        DisplayEnsureResult result,
        string reason)
    {
        operationLog.Add(new DisplayOperationLogEntry(
            Operation: "prepare-virtual-display",
            DisplayId: displayId,
            Width: width,
            Height: height,
            RefreshHz: refreshHz,
            Primary: after?.IsPrimary(displayId),
            HdrEnabled: result.HdrEnabled,
            Reason: result.Success ? reason : $"{reason}: {result.Error}",
            Before: before,
            After: after));
    }

    private void LogRestore(
        DisplayTopologySnapshot? before,
        DisplayTopologySnapshot? after,
        DisplayRestoreResult result,
        string reason)
    {
        operationLog.Add(new DisplayOperationLogEntry(
            Operation: "restore-physical-primary",
            DisplayId: "physical",
            Width: null,
            Height: null,
            RefreshHz: null,
            Primary: after?.PhysicalPrimaryVerified,
            HdrEnabled: null,
            Reason: result.Success ? reason : $"{reason}: {result.Error}",
            Before: before,
            After: after));
    }
}
