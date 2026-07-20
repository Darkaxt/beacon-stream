namespace Beacon.HostAgent.DriverUpdates;

internal sealed record SudoVdaPackageManifest(
    int SchemaVersion,
    string PackageVersion,
    string Architecture,
    string HardwareId,
    string ProtocolVersion,
    string SignerSubject,
    string SignerThumbprint,
    string InfPath,
    IReadOnlyList<SudoVdaPackageFile> Files);

internal sealed record SudoVdaPackageFile(string Path, string Sha256);

internal sealed record SudoVdaPackagePaths(string Inbox, string Staged);

internal sealed record SudoVdaPackagePolicy(
    string Architecture,
    string HardwareId,
    Version MinimumProtocolVersion,
    string SignerSubject,
    string SignerThumbprint);

internal sealed record SudoVdaValidatedPackage(
    string PackageId,
    string PackageVersion,
    string ProtocolVersion,
    string HardwareId,
    string PackageRoot,
    string InfPath,
    string SignerSubject,
    string SignerThumbprint,
    IReadOnlyDictionary<string, string> FileHashes);

internal interface ISudoVdaPackageValidator
{
    Task<SudoVdaValidatedPackage> ValidateAndStageAsync(
        string packageId,
        Guid transactionId,
        CancellationToken cancellationToken);
}

internal sealed record SudoVdaSignatureEvidence(
    bool Valid,
    string Subject,
    string Thumbprint,
    string Diagnostic);

internal interface ISudoVdaSignatureVerifier
{
    SudoVdaSignatureEvidence Verify(string path);
}

internal sealed class SudoVdaPackageValidationException : IOException
{
    public SudoVdaPackageValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
