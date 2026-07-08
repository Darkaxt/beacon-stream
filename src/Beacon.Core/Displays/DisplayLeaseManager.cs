using System.Globalization;
using Beacon.Core.Clients;
using Beacon.Core.Diagnostics;

namespace Beacon.Core.Displays;

public sealed class DisplayLeaseManager(IDisplayBackend displayBackend, IDiagnosticEventSink? diagnostics = null)
{
    public Task<DisplayLeaseResult> PrepareLeaseAsync(ClientProfile profile, CancellationToken cancellationToken) =>
        EnsureLeaseCoreAsync(profile, prepareOnly: true, cancellationToken);

    public async Task<DisplayLeaseResult> EnsureLeaseAsync(ClientProfile profile, CancellationToken cancellationToken)
    {
        return await EnsureLeaseCoreAsync(profile, prepareOnly: false, cancellationToken);
    }

    private async Task<DisplayLeaseResult> EnsureLeaseCoreAsync(
        ClientProfile profile,
        bool prepareOnly,
        CancellationToken cancellationToken)
    {
        string displayId = DisplayLease.CreateDisplayId(profile.ClientId);
        string operation = prepareOnly ? "lease.prepare" : "lease.ensure";
        string repairOperation = $"{operation}.repair";
        DisplayEnsureResult ensureResult = await ApplyDisplayLeaseAsync(
            profile,
            displayId,
            prepareOnly,
            cancellationToken);

        if (!ensureResult.Success)
        {
            Publish(
                DiagnosticSeverity.Error,
                operation,
                prepareOnly
                    ? $"Virtual display prepare failed: {ensureResult.Error}"
                    : $"Virtual display ensure failed: {ensureResult.Error}",
                profile.ClientId.Value,
                displayId,
                DisplayMetadata(profile));

            DisplayRestoreResult restoreResult = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
            if (!restoreResult.Success)
            {
                Publish(
                    DiagnosticSeverity.Error,
                    repairOperation,
                    prepareOnly
                        ? $"Display prepare repair failed because physical primary restore failed: {restoreResult.Error}"
                        : $"Display preflight repair failed because physical primary restore failed: {restoreResult.Error}",
                    profile.ClientId.Value,
                    displayId,
                    DisplayMetadata(profile));
                return new DisplayLeaseResult(
                    false,
                    null,
                    $"{ensureResult.Error}; repair failed because physical primary restore failed: {restoreResult.Error}; refusing to fall back to physical display.");
            }

            Publish(
                DiagnosticSeverity.Information,
                repairOperation,
                prepareOnly
                    ? "Physical primary restore requested before retrying virtual display prepare."
                    : "Physical primary restore requested before retrying virtual display ensure.",
                profile.ClientId.Value,
                displayId,
                DisplayMetadata(profile));

            ensureResult = await ApplyDisplayLeaseAsync(
                profile,
                displayId,
                prepareOnly,
                cancellationToken);

            if (!ensureResult.Success)
            {
                Publish(
                    DiagnosticSeverity.Error,
                    repairOperation,
                    prepareOnly
                        ? $"Virtual display prepare still failed after repair: {ensureResult.Error}"
                        : $"Virtual display ensure still failed after repair: {ensureResult.Error}",
                    profile.ClientId.Value,
                    displayId,
                    DisplayMetadata(profile));
                return new DisplayLeaseResult(
                    false,
                    null,
                    prepareOnly
                        ? $"After repair, virtual display prepare still failed: {ensureResult.Error}; refusing to fall back to physical display."
                        : $"After repair, virtual display ensure still failed: {ensureResult.Error}; refusing to fall back to physical display.");
            }
        }

        var lease = new DisplayLease(
            displayId,
            profile.ClientId,
            profile.Display.PreferredWidth,
            profile.Display.PreferredHeight,
            profile.Display.PreferredRefreshHz);

        Publish(
            DiagnosticSeverity.Information,
            operation,
            prepareOnly ? "Virtual display lease prepared." : "Virtual display lease ensured.",
            profile.ClientId.Value,
            displayId,
            DisplayMetadata(profile));

        return new DisplayLeaseResult(true, lease, null);
    }

    private Task<DisplayEnsureResult> ApplyDisplayLeaseAsync(
        ClientProfile profile,
        string displayId,
        bool prepareOnly,
        CancellationToken cancellationToken) =>
        prepareOnly
            ? displayBackend.PrepareVirtualDisplayAsync(
                displayId,
                profile.Display.PreferredWidth,
                profile.Display.PreferredHeight,
                profile.Display.PreferredRefreshHz,
                profile.Display.HdrPreference,
                cancellationToken)
            : displayBackend.EnsureVirtualDisplayAsync(
                displayId,
                profile.Display.PreferredWidth,
                profile.Display.PreferredHeight,
                profile.Display.PreferredRefreshHz,
                profile.Display.HdrPreference,
                cancellationToken);

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
