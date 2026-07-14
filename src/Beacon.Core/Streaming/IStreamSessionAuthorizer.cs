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

public sealed class FakeStreamSessionAuthorizer : IStreamSessionAuthorizer
{
    private static readonly byte[] InstanceId = [0x42, 0x45, 0x41, 0x43, 0x4f, 0x4e];

    public List<StreamRuntimeAuthorization> Authorizations { get; } = [];

    public List<StreamRuntimeRevocation> Revocations { get; } = [];

    public Task<StreamRuntimeAuthorizationContext> GetContextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new StreamRuntimeAuthorizationContext((byte[])InstanceId.Clone(), 1));

    public Task<StreamRuntimeAuthorizationResult> AuthorizeAsync(
        StreamRuntimeAuthorization authorization,
        CancellationToken cancellationToken)
    {
        Authorizations.Add(authorization);
        return Task.FromResult(StreamRuntimeAuthorizationResult.Accepted);
    }

    public Task<StreamRuntimeAuthorizationResult> RevokeAsync(
        StreamRuntimeRevocation revocation,
        CancellationToken cancellationToken)
    {
        Revocations.Add(revocation);
        return Task.FromResult(StreamRuntimeAuthorizationResult.Accepted);
    }
}
