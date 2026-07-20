namespace Beacon.HostAgent.Contracts;

public sealed record InstallStagedSudoVdaPackagePayload(
    string PackageId,
    Guid TransactionId,
    int ReportedActiveLeaseCount);

public sealed record QuerySudoVdaUpdatePayload(Guid TransactionId);

public enum SudoVdaUpdateState
{
    Accepted,
    Validating,
    Installing,
    Verifying,
    RollingBack,
    Rejected,
    Succeeded,
    RolledBack,
    Degraded
}

public sealed record SudoVdaDriverEvidencePayload(
    string DeviceInstanceId,
    string HardwareId,
    string PublishedInf,
    string DriverVersion,
    string ProtocolVersion,
    string SignerSubject,
    string SignerThumbprint,
    string BinarySha256);

public sealed record SudoVdaUpdatePayload(
    Guid TransactionId,
    string PackageId,
    SudoVdaUpdateState State,
    string Diagnostic,
    SudoVdaDriverEvidencePayload? PreviousEvidence,
    SudoVdaDriverEvidencePayload? ActiveEvidence);
