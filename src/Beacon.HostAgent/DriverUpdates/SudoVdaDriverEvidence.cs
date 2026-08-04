using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent.DriverUpdates;

internal sealed record SudoVdaDriverEvidence(
    string DeviceInstanceId,
    string HardwareId,
    string PublishedInf,
    string DriverVersion,
    string ProtocolVersion,
    string SignerSubject,
    string SignerThumbprint,
    string BinarySha256,
    bool DeviceHealthy)
{
    public SudoVdaDriverEvidencePayload ToPayload() => new(
        DeviceInstanceId,
        HardwareId,
        PublishedInf,
        DriverVersion,
        ProtocolVersion,
        SignerSubject,
        SignerThumbprint,
        BinarySha256);
}

internal sealed record SudoVdaUpdateHostState(
    int ActiveLeaseCount,
    int ActiveVirtualDisplayCount);

internal interface ISudoVdaUpdatePlatform
{
    Task<SudoVdaUpdateHostState> QueryHostStateAsync();

    Task<SudoVdaDriverEvidence> QueryActiveEvidenceAsync();

    Task ExportActivePackageAsync(
        SudoVdaDriverEvidence evidence,
        string destinationRoot);

    Task InstallAsync(SudoVdaValidatedPackage package);

    Task RestartAsync(string deviceInstanceId);

    Task RollbackAsync(
        string exportedPackageRoot,
        SudoVdaDriverEvidence previousEvidence);
}
