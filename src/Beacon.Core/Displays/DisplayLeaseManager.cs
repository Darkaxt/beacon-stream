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
        if (clientActive || ownedProcessRunning || ownedWindowRemaining)
        {
            return false;
        }

        await displayBackend.RemoveVirtualDisplayAsync(displayId, cancellationToken);
        return true;
    }
}
