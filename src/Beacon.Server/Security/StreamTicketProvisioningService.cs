using Beacon.Core.Streaming;

namespace Beacon.Server.Security;

public sealed record StreamTicketProvisioningResult(
    bool Success,
    IssuedStreamTicket? Ticket,
    string? Error)
{
    public static StreamTicketProvisioningResult Provisioned(IssuedStreamTicket ticket) =>
        new(true, ticket, null);

    public static StreamTicketProvisioningResult Fail(string error) => new(false, null, error);
}

public sealed class StreamTicketProvisioningService(
    StreamTicketService tickets,
    IStreamSessionAuthorizer authorizer)
{
    public async Task<StreamTicketProvisioningResult> ProvisionAsync(
        string clientId,
        string sessionId,
        ulong planRevision,
        CancellationToken cancellationToken)
    {
        IssuedStreamTicket? issued = null;
        try
        {
            StreamRuntimeAuthorizationContext context = await authorizer.GetContextAsync(cancellationToken);
            issued = tickets.ReplaceForReconnect(
                clientId,
                sessionId,
                planRevision,
                context.RuntimeInstanceId,
                context.RuntimeGeneration,
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(2));

            string? revocationError = await SendPendingRevocationsAsync(
                clientId,
                sessionId,
                cancellationToken);
            if (revocationError is not null)
            {
                tickets.Revoke(issued.TicketId);
                return StreamTicketProvisioningResult.Fail(revocationError);
            }

            StreamTicketAuthorization runtimeTicket = tickets.CreateRuntimeAuthorization(issued.TicketId);
            StreamRuntimeAuthorizationResult authorized = await authorizer.AuthorizeAsync(
                new StreamRuntimeAuthorization(
                    sessionId,
                    runtimeTicket.ClientId,
                    runtimeTicket.PlanRevision,
                    runtimeTicket.TicketHash,
                    runtimeTicket.RuntimeInstanceId,
                    context.RuntimeGeneration,
                    runtimeTicket.ExpiresAt),
                cancellationToken);
            if (!authorized.Success)
            {
                tickets.Revoke(issued.TicketId);
                return StreamTicketProvisioningResult.Fail(
                    authorized.Error ?? "Stream runtime ticket authorization failed.");
            }
            return StreamTicketProvisioningResult.Provisioned(issued);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            if (issued is not null)
            {
                tickets.Revoke(issued.TicketId);
            }
            return StreamTicketProvisioningResult.Fail(
                $"Stream ticket provisioning failed ({error.GetType().Name}).");
        }
    }

    public async Task<StreamTicketProvisioningResult> RevokeSessionAsync(
        string clientId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            tickets.RevokeUnusedForSession(clientId, sessionId);
            string? error = await SendPendingRevocationsAsync(clientId, sessionId, cancellationToken);
            return error is null
                ? new StreamTicketProvisioningResult(true, null, null)
                : StreamTicketProvisioningResult.Fail(error);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return StreamTicketProvisioningResult.Fail(
                $"Stream ticket revocation failed ({error.GetType().Name}).");
        }
    }

    private async Task<string?> SendPendingRevocationsAsync(
        string clientId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        foreach (PendingStreamTicketRevocation pending in
            tickets.GetPendingRuntimeRevocations(clientId, sessionId))
        {
            StreamRuntimeAuthorizationResult revoked = await authorizer.RevokeAsync(
                new StreamRuntimeRevocation(
                    pending.SessionId,
                    pending.TicketHash,
                    pending.RuntimeGeneration),
                cancellationToken);
            if (!revoked.Success)
            {
                return revoked.Error ?? "Stream runtime ticket revocation failed.";
            }
            tickets.MarkRuntimeRevocationSent(pending.TicketId);
        }
        return null;
    }
}
