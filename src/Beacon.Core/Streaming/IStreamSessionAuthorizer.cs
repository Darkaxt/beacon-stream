namespace Beacon.Core.Streaming;

public sealed record StreamRuntimeAuthorizationContext(
    byte[] RuntimeInstanceId,
    long RuntimeGeneration);

public sealed record StreamRuntimeAuthorization(
    string SessionId,
    string ClientId,
    ulong PlanRevision,
    byte[] TicketHash,
    byte[] RuntimeInstanceId,
    long RuntimeGeneration,
    DateTimeOffset ExpiresAt);

public sealed record StreamRuntimeAuthorizationResult(bool Success, string? Error)
{
    public static StreamRuntimeAuthorizationResult Accepted { get; } = new(true, null);

    public static StreamRuntimeAuthorizationResult Reject(string error) => new(false, error);
}

public sealed record StreamRuntimeRevocation(
    string SessionId,
    byte[] TicketHash,
    long RuntimeGeneration);

public interface IStreamSessionAuthorizer
{
    Task<StreamRuntimeAuthorizationContext> GetContextAsync(CancellationToken cancellationToken);

    Task<StreamRuntimeAuthorizationResult> AuthorizeAsync(
        StreamRuntimeAuthorization authorization,
        CancellationToken cancellationToken);

    Task<StreamRuntimeAuthorizationResult> RevokeAsync(
        StreamRuntimeRevocation revocation,
        CancellationToken cancellationToken);
}
