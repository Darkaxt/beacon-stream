namespace Beacon.HostAgent.Contracts;

public sealed record InstallStagedHostAgentPackagePayload(
    string PackageId,
    Guid TransactionId);

public sealed record QueryHostAgentUpdatePayload(Guid TransactionId);

public enum HostAgentUpdateState
{
    Accepted,
    Validating,
    Staged,
    Installing,
    AwaitingReadiness,
    Succeeded,
    RolledBack,
    Degraded
}

public sealed record HostAgentUpdatePayload(
    Guid TransactionId,
    string PackageId,
    HostAgentUpdateState State,
    string Diagnostic,
    string SourceCommit,
    string? PreviousVersionId,
    string? ActiveVersionId);
