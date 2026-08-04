using Beacon.Core.Streaming;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Google.Protobuf;

namespace Beacon.Platform.Windows.Streaming;

public sealed class StreamWorkerSessionAuthorizer(IStreamWorkerHost host) : IStreamSessionAuthorizer
{
    public async Task<StreamRuntimeAuthorizationContext> GetContextAsync(CancellationToken cancellationToken)
    {
        await host.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        long generation = host.CurrentProcessGeneration;
        byte[] instanceId = host.WorkerInstanceId.ToArray();
        if (generation <= 0
            || instanceId.Length == 0
            || !host.IsCurrentProcessGeneration(generation))
        {
            throw new StreamWorkerProtocolException("StreamWorker runtime identity is unavailable.");
        }
        return new StreamRuntimeAuthorizationContext(instanceId, generation);
    }

    public async Task<StreamRuntimeAuthorizationResult> AuthorizeAsync(
        StreamRuntimeAuthorization authorization,
        CancellationToken cancellationToken)
    {
        StreamWorkerCommandResponse response = await host.SendAsync(
            authorization.RuntimeGeneration,
            new WorkerIpcEnvelope
            {
                SessionId = authorization.SessionId,
                AuthorizeTicket = new AuthorizeTicket
                {
                    ClientId = authorization.ClientId,
                    PlanRevision = authorization.PlanRevision,
                    TicketHash = ByteString.CopyFrom(authorization.TicketHash),
                    WorkerInstanceId = ByteString.CopyFrom(authorization.RuntimeInstanceId),
                    ExpiresAtUnixMs = checked((ulong)authorization.ExpiresAt.ToUnixTimeMilliseconds()),
                },
            },
            cancellationToken).ConfigureAwait(false);
        WorkerCompletion completion = response.Completion.WorkerCompletion;
        return completion.Succeeded
            ? StreamRuntimeAuthorizationResult.Accepted
            : StreamRuntimeAuthorizationResult.Reject(
                $"StreamWorker rejected ticket authorization ({completion.ErrorCode}).");
    }

    public async Task<StreamRuntimeAuthorizationResult> RevokeAsync(
        StreamRuntimeRevocation revocation,
        CancellationToken cancellationToken)
    {
        StreamWorkerCommandResponse response;
        try
        {
            response = await host.SendAsync(
                revocation.RuntimeGeneration,
                new WorkerIpcEnvelope
                {
                    SessionId = revocation.SessionId,
                    RevokeTicket = new RevokeTicket
                    {
                        TicketHash = ByteString.CopyFrom(revocation.TicketHash),
                    },
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (StreamWorkerGenerationChangedException)
        {
            return StreamRuntimeAuthorizationResult.Accepted;
        }
        WorkerCompletion completion = response.Completion.WorkerCompletion;
        return completion.Succeeded
            ? StreamRuntimeAuthorizationResult.Accepted
            : StreamRuntimeAuthorizationResult.Reject(
                $"StreamWorker rejected ticket revocation ({completion.ErrorCode}).");
    }
}
