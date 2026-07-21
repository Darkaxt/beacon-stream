using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beacon.HostAgent.Update;

public sealed record HostAgentUpdateManifest(
    int SchemaVersion,
    string PackageId,
    string SourceCommit,
    string Target,
    string Architecture,
    string MinimumBootstrapVersion,
    string EntryPoint,
    IReadOnlyList<HostAgentUpdateFile> Files);

public sealed record HostAgentUpdateFile(string Path, long Length, string Sha256);

public sealed record HostAgentUpdateBuildIdentity(
    string PackageId,
    string SourceCommit,
    string MinimumBootstrapVersion);

public sealed record HostAgentBuiltPackage(
    string PackageRoot,
    HostAgentUpdateManifest Manifest,
    byte[] ManifestBytes);

public sealed record HostAgentValidatedPackage(
    string PackageRoot,
    HostAgentUpdateManifest Manifest,
    byte[] ManifestBytes,
    IReadOnlyDictionary<string, string> FileHashes);

public interface IHostAgentUpdatePackageValidator
{
    Task<HostAgentValidatedPackage> ValidateAsync(
        string packageRoot,
        CancellationToken cancellationToken);
}

public sealed class HostAgentUpdateValidationException : IOException
{
    public HostAgentUpdateValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

internal static class HostAgentUpdateJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}
