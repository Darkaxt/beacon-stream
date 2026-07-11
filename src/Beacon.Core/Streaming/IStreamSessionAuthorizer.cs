namespace Beacon.Core.Streaming;

public sealed record StreamWorkerAuthorizationContext(byte[] WorkerInstanceId);

public sealed record StreamWorkerAuthorization(
    string SessionId,
    string ClientId,
    ulong PlanRevision,
    byte[] TicketHash,
    byte[] WorkerInstanceId,
    DateTimeOffset ExpiresAt);

public sealed record StreamWorkerAuthorizationResult(bool Success, string? Error)
{
    public static StreamWorkerAuthorizationResult Accepted { get; } = new(true, null);

    public static StreamWorkerAuthorizationResult Reject(string error) => new(false, error);
}

public sealed record StreamWorkerRevocation(string SessionId, byte[] TicketHash);

public interface IStreamSessionAuthorizer
{
    Task<StreamWorkerAuthorizationContext> GetContextAsync(CancellationToken cancellationToken);

    Task<StreamWorkerAuthorizationResult> AuthorizeAsync(
        StreamWorkerAuthorization authorization,
        CancellationToken cancellationToken);

    Task<StreamWorkerAuthorizationResult> RevokeAsync(
        StreamWorkerRevocation revocation,
        CancellationToken cancellationToken);
}

public sealed class FakeStreamSessionAuthorizer : IStreamSessionAuthorizer
{
    private static readonly byte[] InstanceId = [0x42, 0x45, 0x41, 0x43, 0x4f, 0x4e];

    public List<StreamWorkerAuthorization> Authorizations { get; } = [];

    public List<StreamWorkerRevocation> Revocations { get; } = [];

    public Task<StreamWorkerAuthorizationContext> GetContextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new StreamWorkerAuthorizationContext((byte[])InstanceId.Clone()));

    public Task<StreamWorkerAuthorizationResult> AuthorizeAsync(
        StreamWorkerAuthorization authorization,
        CancellationToken cancellationToken)
    {
        Authorizations.Add(authorization);
        return Task.FromResult(StreamWorkerAuthorizationResult.Accepted);
    }

    public Task<StreamWorkerAuthorizationResult> RevokeAsync(
        StreamWorkerRevocation revocation,
        CancellationToken cancellationToken)
    {
        Revocations.Add(revocation);
        return Task.FromResult(StreamWorkerAuthorizationResult.Accepted);
    }
}
