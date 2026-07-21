using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Server.Security;

namespace Beacon.Server.Streaming;

public sealed record StreamSessionReconnectResult(
    bool Success,
    DisplayLease? Lease,
    StreamingSessionState? Stream,
    IssuedStreamTicket? Ticket,
    string? Error)
{
    public static StreamSessionReconnectResult Connected(
        DisplayLease lease,
        StreamingSessionState stream,
        IssuedStreamTicket ticket) =>
        new(true, lease, stream, ticket, null);

    public static StreamSessionReconnectResult Fail(string error) =>
        new(false, null, null, null, error);
}

public sealed class StreamSessionReconnectService(
    DisplayLeaseManager leases,
    IStreamingBackend streaming,
    ISessionOwnershipTracker ownership,
    StreamTicketProvisioningService ticketProvisioning)
{
    public async Task<StreamSessionReconnectResult> ReconnectAsync(
        string clientId,
        ClientProfile profile,
        SessionPlan plan,
        CancellationToken cancellationToken)
    {
        StreamingSessionState? streamingSession = await streaming.GetSessionAsync(
            plan.SessionId,
            cancellationToken);
        bool restartRequired = !IsActiveRuntime(streamingSession);
        if (restartRequired)
        {
            SessionOwnershipSnapshot? ownershipSnapshot = await ownership.GetSnapshotAsync(
                plan.SessionId,
                cancellationToken);
            if (ownershipSnapshot is null)
            {
                return StreamSessionReconnectResult.Fail(
                    $"Stream session '{plan.SessionId}' has no active streaming runtime or launched-session ownership.");
            }

            StreamingPreflightResult preflight = await streaming.CheckReadinessAsync(
                plan,
                cancellationToken);
            if (!preflight.Success)
            {
                return StreamSessionReconnectResult.Fail(
                    preflight.Error ?? "Streaming runtime preflight failed.");
            }
        }

        DisplayLeaseResult leaseResult = await leases.PrepareLeaseAsync(profile, cancellationToken);
        if (!leaseResult.Success || leaseResult.Lease is null)
        {
            return StreamSessionReconnectResult.Fail(
                leaseResult.Error ?? "Display lease preparation failed.");
        }

        if (restartRequired)
        {
            StreamingStartResult restarted = await streaming.StartAsync(plan, cancellationToken);
            if (!restarted.Success || !IsActiveRuntime(restarted.Session))
            {
                string detail = restarted.Error
                    ?? $"Stream session '{plan.SessionId}' restart did not publish an active runtime.";
                if (restarted.Session is { RuntimeGeneration: var generation }
                    && generation != Guid.Empty)
                {
                    detail += await StopRestartedRuntimeAsync(
                        plan.SessionId,
                        generation,
                        "invalid restarted runtime metadata");
                }
                return StreamSessionReconnectResult.Fail(detail);
            }

            streamingSession = restarted.Session;
        }
        StreamingSessionState activeStreamingSession = streamingSession!;

        StreamTicketProvisioningResult ticketResult;
        try
        {
            ticketResult = await ticketProvisioning.ProvisionAsync(
                clientId,
                plan.SessionId,
                plan.Revision,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            string detail = "Reconnect ticket provisioning canceled unexpectedly.";
            detail += await RevokeSessionTicketsAsync(
                clientId,
                plan.SessionId,
                "canceled reconnect ticket provisioning");
            if (restartRequired)
            {
                detail += await StopRestartedRuntimeAsync(
                    plan.SessionId,
                    activeStreamingSession.RuntimeGeneration,
                    "canceled fresh ticket provisioning");
            }
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            return StreamSessionReconnectResult.Fail(detail);
        }
        if (!ticketResult.Success || ticketResult.Ticket is null)
        {
            string detail = ticketResult.Error ?? "Reconnect ticket provisioning failed.";
            if (restartRequired)
            {
                detail += await StopRestartedRuntimeAsync(
                    plan.SessionId,
                    activeStreamingSession.RuntimeGeneration,
                    "fresh ticket provisioning failure");
            }
            return StreamSessionReconnectResult.Fail(detail);
        }

        StreamingSessionState? confirmedStreamingSession = await streaming.GetSessionAsync(
            plan.SessionId,
            cancellationToken);
        if (!IsSameActiveRuntime(activeStreamingSession, confirmedStreamingSession))
        {
            StreamTicketProvisioningResult revoked = await ticketProvisioning.RevokeSessionAsync(
                clientId,
                plan.SessionId,
                cancellationToken);
            string revocationStatus = revoked.Success
                ? "Reconnect ticket revoked."
                : $"Reconnect ticket revocation failed: {revoked.Error}";
            return StreamSessionReconnectResult.Fail(
                $"Stream session '{plan.SessionId}' changed while provisioning reconnect ticket. " +
                revocationStatus);
        }

        return StreamSessionReconnectResult.Connected(
            leaseResult.Lease,
            confirmedStreamingSession!,
            ticketResult.Ticket);
    }

    private async Task<string> RevokeSessionTicketsAsync(
        string clientId,
        string sessionId,
        string reason)
    {
        try
        {
            StreamTicketProvisioningResult revoked = await ticketProvisioning.RevokeSessionAsync(
                clientId,
                sessionId,
                CancellationToken.None);
            return revoked.Success
                ? $" Stream tickets revoked after {reason}."
                : $" Stream ticket revocation failed after {reason}: {revoked.Error}.";
        }
        catch (Exception error)
        {
            return $" Stream ticket revocation failed after {reason} ({error.GetType().Name}).";
        }
    }

    private async Task<string> StopRestartedRuntimeAsync(
        string sessionId,
        Guid runtimeGeneration,
        string reason)
    {
        try
        {
            StreamingStopResult stopped = await streaming.StopRuntimeAsync(
                sessionId,
                runtimeGeneration,
                CancellationToken.None);
            return stopped.Success
                ? $" Restarted streaming runtime stopped after {reason}."
                : $" Restarted streaming runtime stop failed after {reason}: {stopped.Error}.";
        }
        catch (Exception error)
        {
            return $" Restarted streaming runtime stop failed after {reason} ({error.GetType().Name}).";
        }
    }

    private static bool IsSameActiveRuntime(
        StreamingSessionState expected,
        StreamingSessionState? actual) =>
        actual is not null
        && IsActiveRuntime(actual)
        && string.Equals(actual.SessionId, expected.SessionId, StringComparison.Ordinal)
        && actual.ActiveListenerPort == expected.ActiveListenerPort
        && actual.RuntimeGeneration == expected.RuntimeGeneration;

    private static bool IsActiveRuntime(StreamingSessionState? session) =>
        session is not null
        && string.Equals(session.State, "running", StringComparison.Ordinal)
        && session.ActiveListenerPort is > 0 and <= 65_535
        && session.RuntimeGeneration != Guid.Empty;
}
