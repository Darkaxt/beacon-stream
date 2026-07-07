using Beacon.Core.Clients;

namespace Beacon.Core.Displays;

public sealed class DisplayLeaseManager(IDisplayBackend displayBackend)
{
    public async Task<DisplayLeaseResult> EnsureLeaseAsync(ClientProfile profile, CancellationToken cancellationToken)
    {
        string displayId = DisplayLease.CreateDisplayId(profile.ClientId);
        DisplayEnsureResult ensureResult = await displayBackend.EnsureVirtualDisplayAsync(
            displayId,
            profile.Display.PreferredWidth,
            profile.Display.PreferredHeight,
            profile.Display.PreferredRefreshHz,
            profile.Display.HdrPreference,
            cancellationToken);

        if (!ensureResult.Success)
        {
            return new DisplayLeaseResult(
                false,
                null,
                $"{ensureResult.Error}; refusing to fall back to physical display.");
        }

        var lease = new DisplayLease(
            displayId,
            profile.ClientId,
            profile.Display.PreferredWidth,
            profile.Display.PreferredHeight,
            profile.Display.PreferredRefreshHz);

        return new DisplayLeaseResult(true, lease, null);
    }

    public Task DisconnectAsync(string displayId, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<bool> CleanupIfAllowedAsync(
        string displayId,
        bool clientActive,
        bool ownedProcessRunning,
        bool ownedWindowRemaining,
        CancellationToken cancellationToken)
    {
        if (clientActive)
        {
            return false;
        }

        DisplayRestoreResult restoreResult = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
        if (!restoreResult.Success)
        {
            return false;
        }

        if (ownedProcessRunning || ownedWindowRemaining)
        {
            return false;
        }

        DisplayRemoveResult removeResult = await displayBackend.RemoveVirtualDisplayAsync(displayId, cancellationToken);
        return removeResult.Success;
    }

    public async Task<DisplayRecoveryResult> RecoverDisplayAsync(string displayId, CancellationToken cancellationToken)
    {
        DisplayRestoreResult restoreResult = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
        if (!restoreResult.Success)
        {
            return DisplayRecoveryResult.Fail(restoreResult.Error ?? "Physical primary restore failed.");
        }

        DisplayRemoveResult removeResult = await displayBackend.RemoveVirtualDisplayAsync(displayId, cancellationToken);
        if (!removeResult.Success)
        {
            return DisplayRecoveryResult.Fail(removeResult.Error ?? $"Virtual display {displayId} removal failed.");
        }

        return DisplayRecoveryResult.Ok();
    }
}
