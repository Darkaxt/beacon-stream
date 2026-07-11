using Beacon.Core.Streaming;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Google.Protobuf;

namespace Beacon.Platform.Windows.Streaming;

public sealed class StreamWorkerSessionAuthorizer(IStreamWorkerHost host) : IStreamSessionAuthorizer
{
    public async Task<StreamWorkerAuthorizationContext> GetContextAsync(CancellationToken cancellationToken)
    {
        await host.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        byte[] instanceId = host.WorkerInstanceId.ToArray();
        if (instanceId.Length == 0)
        {
            throw new StreamWorkerProtocolException("StreamWorker did not provide an instance id.");
        }
        return new StreamWorkerAuthorizationContext(instanceId);
    }

    public async Task<StreamWorkerAuthorizationResult> AuthorizeAsync(
        StreamWorkerAuthorization authorization,
        CancellationToken cancellationToken)
    {
        StreamWorkerCommandResponse response = await host.SendAsync(
            new WorkerIpcEnvelope
            {
                SessionId = authorization.SessionId,
                AuthorizeTicket = new AuthorizeTicket
                {
                    ClientId = authorization.ClientId,
                    PlanRevision = authorization.PlanRevision,
                    TicketHash = ByteString.CopyFrom(authorization.TicketHash),
                    WorkerInstanceId = ByteString.CopyFrom(authorization.WorkerInstanceId),
                    ExpiresAtUnixMs = checked((ulong)authorization.ExpiresAt.ToUnixTimeMilliseconds()),
                },
            },
            cancellationToken).ConfigureAwait(false);
        WorkerCompletion completion = response.Completion.WorkerCompletion;
        return completion.Succeeded
            ? StreamWorkerAuthorizationResult.Accepted
            : StreamWorkerAuthorizationResult.Reject(
                $"StreamWorker rejected ticket authorization ({completion.ErrorCode}).");
    }

    public async Task<StreamWorkerAuthorizationResult> RevokeAsync(
        StreamWorkerRevocation revocation,
        CancellationToken cancellationToken)
    {
        StreamWorkerCommandResponse response = await host.SendAsync(
            new WorkerIpcEnvelope
            {
                SessionId = revocation.SessionId,
                RevokeTicket = new RevokeTicket
                {
                    TicketHash = ByteString.CopyFrom(revocation.TicketHash),
                },
            },
            cancellationToken).ConfigureAwait(false);
        WorkerCompletion completion = response.Completion.WorkerCompletion;
        return completion.Succeeded
            ? StreamWorkerAuthorizationResult.Accepted
            : StreamWorkerAuthorizationResult.Reject(
                $"StreamWorker rejected ticket revocation ({completion.ErrorCode}).");
    }
}
