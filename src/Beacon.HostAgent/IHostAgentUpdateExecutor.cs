using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent;

internal interface IHostAgentUpdateExecutor
{
    Task<HostAgentUpdatePayload> StageAsync(
        string packageId,
        Guid transactionId,
        CancellationToken cancellationToken);

    bool TryGet(Guid transactionId, out HostAgentUpdatePayload? value);
}
