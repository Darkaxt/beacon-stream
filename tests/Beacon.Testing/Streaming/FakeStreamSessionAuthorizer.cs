namespace Beacon.Core.Streaming;

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
