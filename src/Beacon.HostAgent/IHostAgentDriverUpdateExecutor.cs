using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent;

internal interface IHostAgentDriverUpdateExecutor
{
    SudoVdaUpdatePayload Start(
        string packageId,
        Guid transactionId,
        int reportedActiveLeaseCount);

    bool TryGet(Guid transactionId, out SudoVdaUpdatePayload? value);
}
