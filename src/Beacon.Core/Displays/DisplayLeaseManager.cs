using System.Globalization;
using Beacon.Core.Clients;
using Beacon.Core.Diagnostics;

namespace Beacon.Core.Displays;

public sealed class DisplayLeaseManager(IDisplayBackend displayBackend, IDiagnosticEventSink? diagnostics = null)
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
            Publish(
                DiagnosticSeverity.Error,
                "lease.ensure",
                $"Virtual display ensure failed: {ensureResult.Error}",
                profile.ClientId.Value,
                displayId,
                DisplayMetadata(profile));
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

        Publish(
            DiagnosticSeverity.Information,
            "lease.ensure",
            "Virtual display lease ensured.",
            profile.ClientId.Value,
            displayId,
            DisplayMetadata(profile));

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
            Publish(
                DiagnosticSeverity.Information,
                "lease.cleanup.retained",
                "Display lease retained because the client is still active.",
                clientId: null,
                displayId,
                CleanupMetadata(clientActive, ownedProcessRunning, ownedWindowRemaining));
            return false;
        }

        DisplayRestoreResult restoreResult = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
        if (!restoreResult.Success)
        {
            Publish(
                DiagnosticSeverity.Error,
                "lease.cleanup.restore-failed",
                $"Physical primary restore failed during cleanup: {restoreResult.Error}",
                clientId: null,
                displayId,
                CleanupMetadata(clientActive, ownedProcessRunning, ownedWindowRemaining));
            return false;
        }

        if (ownedProcessRunning || ownedWindowRemaining)
        {
            Publish(
                DiagnosticSeverity.Information,
                "lease.cleanup.retained",
                "Display lease retained because owned session work is still present.",
                clientId: null,
                displayId,
                CleanupMetadata(clientActive, ownedProcessRunning, ownedWindowRemaining));
            return false;
        }

        DisplayRemoveResult removeResult = await displayBackend.RemoveVirtualDisplayAsync(displayId, cancellationToken);
        Publish(
            removeResult.Success ? DiagnosticSeverity.Information : DiagnosticSeverity.Error,
            removeResult.Success ? "lease.cleanup.removed" : "lease.cleanup.remove-failed",
            removeResult.Success
                ? "Virtual display lease removed after cleanup."
                : $"Virtual display removal failed during cleanup: {removeResult.Error}",
            clientId: null,
            displayId,
            CleanupMetadata(clientActive, ownedProcessRunning, ownedWindowRemaining));
        return removeResult.Success;
    }

    public async Task<DisplayRecoveryResult> RecoverDisplayAsync(string displayId, CancellationToken cancellationToken)
    {
        DisplayRestoreResult restoreResult = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
        if (!restoreResult.Success)
        {
            Publish(
                DiagnosticSeverity.Error,
                "lease.recover",
                $"Physical primary restore failed during display recovery: {restoreResult.Error}",
                clientId: null,
                displayId,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            return DisplayRecoveryResult.Fail(restoreResult.Error ?? "Physical primary restore failed.");
        }

        DisplayRemoveResult removeResult = await displayBackend.RemoveVirtualDisplayAsync(displayId, cancellationToken);
        if (!removeResult.Success)
        {
            Publish(
                DiagnosticSeverity.Error,
                "lease.recover",
                $"Virtual display removal failed during display recovery: {removeResult.Error}",
                clientId: null,
                displayId,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            return DisplayRecoveryResult.Fail(removeResult.Error ?? $"Virtual display {displayId} removal failed.");
        }

        Publish(
            DiagnosticSeverity.Information,
            "lease.recover",
            "Display recovery restored the physical primary and removed the virtual display.",
            clientId: null,
            displayId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        return DisplayRecoveryResult.Ok();
    }

    private void Publish(
        string severity,
        string operation,
        string message,
        string? clientId,
        string displayId,
        IReadOnlyDictionary<string, string> metadata)
    {
        diagnostics?.Publish(DiagnosticEvent.Create(
            severity,
            "display",
            operation,
            message,
            clientId,
            sessionId: null,
            displayId,
            metadata));
    }

    private static Dictionary<string, string> DisplayMetadata(ClientProfile profile) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["width"] = profile.Display.PreferredWidth.ToString(CultureInfo.InvariantCulture),
            ["height"] = profile.Display.PreferredHeight.ToString(CultureInfo.InvariantCulture),
            ["refreshHz"] = profile.Display.PreferredRefreshHz.ToString(CultureInfo.InvariantCulture),
            ["hdrPreference"] = profile.Display.HdrPreference.ToString()
        };

    private static Dictionary<string, string> CleanupMetadata(
        bool clientActive,
        bool ownedProcessRunning,
        bool ownedWindowRemaining) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["clientActive"] = clientActive.ToString().ToLowerInvariant(),
            ["ownedProcessRunning"] = ownedProcessRunning.ToString().ToLowerInvariant(),
            ["ownedWindowRemaining"] = ownedWindowRemaining.ToString().ToLowerInvariant()
        };
}
