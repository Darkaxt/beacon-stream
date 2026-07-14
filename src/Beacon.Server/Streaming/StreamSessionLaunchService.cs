using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Games;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Server.Security;

namespace Beacon.Server.Streaming;

public sealed record StreamSessionLaunchResult(
    bool Success,
    DisplayLease? Lease,
    GameLaunchState? Launch,
    StreamingSessionState? Stream,
    IssuedStreamTicket? Ticket,
    string? Error)
{
    public static StreamSessionLaunchResult Started(
        DisplayLease lease,
        GameLaunchState launch,
        StreamingSessionState stream,
        IssuedStreamTicket ticket) =>
        new(true, lease, launch, stream, ticket, null);

    public static StreamSessionLaunchResult Fail(string error) =>
        new(false, null, null, null, null, error);
}

public sealed class StreamSessionLaunchService(
    DisplayLeaseManager leases,
    IDisplayBackend displayBackend,
    IGameLauncher launcher,
    ISessionOwnershipTracker ownership,
    IStreamingBackend streaming,
    StreamTicketProvisioningService ticketProvisioning)
{
    public async Task<StreamSessionLaunchResult> LaunchAsync(
        string clientId,
        ClientProfile profile,
        GameDescriptor game,
        SessionPlan plan,
        CancellationToken cancellationToken)
    {
        StreamingPreflightResult preflight = await streaming.CheckReadinessAsync(
            plan,
            cancellationToken);
        if (!preflight.Success)
        {
            return StreamSessionLaunchResult.Fail(
                preflight.Error ?? "Streaming runtime preflight failed.");
        }

        DisplayLeaseResult prepared = await leases.PrepareLeaseAsync(profile, cancellationToken);
        if (!prepared.Success || prepared.Lease is null)
        {
            return StreamSessionLaunchResult.Fail(
                prepared.Error ?? "Display preparation failed.");
        }

        StreamingStartResult started = await streaming.StartAsync(plan, cancellationToken);
        if (!IsActive(started))
        {
            string error = started.Error
                ?? $"Stream session '{plan.SessionId}' has no active streaming runtime.";
            string cleanup = started.Success
                ? await StopRuntimeAsync(
                    plan.SessionId,
                    started.Session?.RuntimeGeneration ?? Guid.Empty,
                    "invalid metadata")
                : string.Empty;
            return StreamSessionLaunchResult.Fail($"{error}{cleanup}");
        }
        StreamingSessionState stream = started.Session!;

        DisplayLeaseResult activated;
        try
        {
            activated = await leases.EnsureLeaseAsync(profile, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await RestorePhysicalAsync("canceled display activation");
            await StopRuntimeAsync(
                plan.SessionId,
                stream.RuntimeGeneration,
                "canceled display activation");
            throw;
        }
        catch (Exception error)
        {
            string restored = await RestorePhysicalAsync("unexpected display activation failure");
            string stopped = await StopRuntimeAsync(
                plan.SessionId,
                stream.RuntimeGeneration,
                "unexpected display activation failure");
            return StreamSessionLaunchResult.Fail(
                $"Display activation failed unexpectedly ({error.GetType().Name}).{restored}{stopped}");
        }
        if (!activated.Success || activated.Lease is null)
        {
            string stopped = await StopRuntimeAsync(
                plan.SessionId,
                stream.RuntimeGeneration,
                "display activation failure");
            return StreamSessionLaunchResult.Fail(
                $"{activated.Error ?? "Display activation failed."}{stopped}");
        }

        GameLaunchResult launched;
        try
        {
            launched = await launcher.LaunchAsync(
                new GameLaunchRequest(game, plan, activated.Lease.DisplayId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await RestorePhysicalAsync("canceled application launch");
            await StopRuntimeAsync(
                plan.SessionId,
                stream.RuntimeGeneration,
                "canceled application launch");
            throw;
        }
        catch (Exception error)
        {
            string restored = await RestorePhysicalAsync("unexpected application launch failure");
            string stopped = await StopRuntimeAsync(
                plan.SessionId,
                stream.RuntimeGeneration,
                "unexpected application launch failure");
            return StreamSessionLaunchResult.Fail(
                $"Application launch failed unexpectedly ({error.GetType().Name}).{restored}{stopped}");
        }
        if (!launched.Success || launched.State is null)
        {
            string restored = await RestorePhysicalAsync("game launch failure");
            string stopped = await StopRuntimeAsync(
                plan.SessionId,
                stream.RuntimeGeneration,
                "game launch failure");
            return StreamSessionLaunchResult.Fail(
                $"{launched.Error ?? "Application launch failed."}{restored}{stopped}");
        }

        try
        {
            await ownership.RecordLaunchAsync(plan, launched.State, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await RecordAndTerminateOwnedWorkAsync(
                plan,
                launched.State,
                "canceled ownership recording");
            await RestorePhysicalAsync("canceled ownership recording");
            await StopRuntimeAsync(
                plan.SessionId,
                stream.RuntimeGeneration,
                "canceled ownership recording");
            throw;
        }
        catch (Exception error)
        {
            string terminated = await RecordAndTerminateOwnedWorkAsync(
                plan,
                launched.State,
                "unexpected ownership recording failure");
            string restored = await RestorePhysicalAsync("unexpected ownership recording failure");
            string stopped = await StopRuntimeAsync(
                plan.SessionId,
                stream.RuntimeGeneration,
                "unexpected ownership recording failure");
            return StreamSessionLaunchResult.Fail(
                $"Ownership recording failed unexpectedly ({error.GetType().Name})." +
                $"{terminated}{restored}{stopped}");
        }
        StreamTicketProvisioningResult provisioned;
        try
        {
            provisioned = await ticketProvisioning.ProvisionAsync(
                clientId,
                plan.SessionId,
                plan.Revision,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            string revoked = await RevokeSessionTicketsAsync(
                clientId,
                plan.SessionId,
                "canceled ticket provisioning");
            string terminated = await TerminateOwnedWorkAsync(
                plan.SessionId,
                "canceled ticket provisioning");
            string restored = await RestorePhysicalAsync("canceled ticket provisioning");
            string stopped = await StopRuntimeAsync(
                plan.SessionId,
                stream.RuntimeGeneration,
                "canceled ticket provisioning");
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return StreamSessionLaunchResult.Fail(
                $"Ticket provisioning canceled unexpectedly.{revoked}{terminated}{restored}{stopped}");
        }
        if (!provisioned.Success || provisioned.Ticket is null)
        {
            string terminated = await TerminateOwnedWorkAsync(
                plan.SessionId,
                "ticket provisioning failure");
            string restored = await RestorePhysicalAsync("ticket provisioning failure");
            string stopped = await StopRuntimeAsync(
                plan.SessionId,
                stream.RuntimeGeneration,
                "ticket provisioning failure");
            return StreamSessionLaunchResult.Fail(
                $"{provisioned.Error ?? "Stream ticket provisioning failed."}" +
                $"{terminated}{restored}{stopped}");
        }

        return StreamSessionLaunchResult.Started(
            activated.Lease,
            launched.State,
            stream,
            provisioned.Ticket);
    }

    private async Task<string> TerminateOwnedWorkAsync(string sessionId, string reason)
    {
        try
        {
            SessionOwnedWorkTerminationResult result = await ownership.TerminateOwnedWorkAsync(
                sessionId,
                CancellationToken.None);
            return result.Success
                ? $" Owned application work terminated after {reason}."
                : $" Owned application cleanup failed after {reason}: {result.Error}.";
        }
        catch (Exception error)
        {
            return $" Owned application cleanup failed after {reason} ({error.GetType().Name}).";
        }
    }

    private async Task<string> RecordAndTerminateOwnedWorkAsync(
        SessionPlan plan,
        GameLaunchState launchState,
        string reason)
    {
        try
        {
            await ownership.RecordLaunchAsync(plan, launchState, CancellationToken.None);
        }
        catch (Exception error)
        {
            return $" Owned application recovery failed after {reason} ({error.GetType().Name}).";
        }

        return await TerminateOwnedWorkAsync(plan.SessionId, reason);
    }

    private async Task<string> RevokeSessionTicketsAsync(
        string clientId,
        string sessionId,
        string reason)
    {
        try
        {
            StreamTicketProvisioningResult result = await ticketProvisioning.RevokeSessionAsync(
                clientId,
                sessionId,
                CancellationToken.None);
            return result.Success
                ? $" Stream tickets revoked after {reason}."
                : $" Stream ticket revocation failed after {reason}: {result.Error}.";
        }
        catch (Exception error)
        {
            return $" Stream ticket revocation failed after {reason} ({error.GetType().Name}).";
        }
    }

    private async Task<string> RestorePhysicalAsync(string reason)
    {
        try
        {
            DisplayRestoreResult result = await displayBackend.RestorePhysicalPrimaryAsync(
                CancellationToken.None);
            return result.Success
                ? $" Physical primary restore requested after {reason}."
                : $" Physical primary restore failed after {reason}: {result.Error}.";
        }
        catch (Exception error)
        {
            return $" Physical primary restore failed after {reason} ({error.GetType().Name}).";
        }
    }

    private async Task<string> StopRuntimeAsync(
        string sessionId,
        Guid generation,
        string reason)
    {
        try
        {
            StreamingStopResult result = await streaming.StopRuntimeAsync(
                sessionId,
                generation,
                CancellationToken.None);
            return result.Success
                ? $" Streaming runtime stopped after {reason}."
                : $" Streaming runtime stop compensation failed after {reason}: {result.Error}.";
        }
        catch (Exception error)
        {
            return $" Streaming runtime stop compensation failed after {reason} ({error.GetType().Name}).";
        }
    }

    private static bool IsActive(StreamingStartResult result) =>
        result.Success
        && result.Session is not null
        && string.Equals(result.Session.State, "running", StringComparison.Ordinal)
        && result.Session.ActiveListenerPort is > 0 and <= 65_535
        && result.Session.RuntimeGeneration != Guid.Empty;
}
