using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beacon.HostAgent.DriverUpdates;

internal sealed class HostAgentDriverUpdateStorage
{
    public HostAgentDriverUpdateStorage(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Host Agent storage root is required.", nameof(root));
        }
        Root = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Directory.CreateDirectory(Root);
        Inbox = CreateChild("Inbox");
        Staged = CreateChild("Staged");
        InstalledEvidence = CreateChild("InstalledEvidence");
        Transactions = CreateChild("Transactions");
        Logs = CreateChild("Logs");
        DriverPolicyPath = Path.Combine(Root, "driver-policy.json");
    }

    public static HostAgentDriverUpdateStorage Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Beacon",
        "HostAgent"));

    public string Root { get; }

    public string Inbox { get; }

    public string Staged { get; }

    public string InstalledEvidence { get; }

    public string Transactions { get; }

    public string Logs { get; }

    public string DriverPolicyPath { get; }

    private string CreateChild(string name)
    {
        string path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}

internal sealed class SudoVdaPackagePolicyStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string path;

    public SudoVdaPackagePolicyStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A fully qualified driver policy path is required.", nameof(path));
        }
        this.path = Path.GetFullPath(path);
    }

    public SudoVdaPackagePolicy LoadOrCreate(SudoVdaSignatureEvidence bootstrapSignature)
    {
        if (File.Exists(path))
        {
            return Read();
        }
        if (!bootstrapSignature.Valid
            || string.IsNullOrWhiteSpace(bootstrapSignature.Subject)
            || string.IsNullOrWhiteSpace(bootstrapSignature.Thumbprint))
        {
            throw new InvalidDataException(
                "A valid installed SudoVDA signer is required to bootstrap driver policy.");
        }

        var document = new PolicyDocument(
            SchemaVersion: 1,
            Architecture: "x64",
            HardwareId: @"ROOT\SudoMaker\SudoVDA",
            MinimumProtocolVersion: "0.2.0",
            bootstrapSignature.Subject,
            NormalizeThumbprint(bootstrapSignature.Thumbprint));
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidDataException("Driver policy directory is invalid.");
        }
        Directory.CreateDirectory(directory);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(document, Json);
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        return ToPolicy(document);
    }

    private SudoVdaPackagePolicy Read()
    {
        PolicyDocument document;
        try
        {
            document = JsonSerializer.Deserialize<PolicyDocument>(File.ReadAllBytes(path), Json)
                ?? throw new InvalidDataException("Driver policy is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Driver policy is invalid.", error);
        }
        return ToPolicy(document);
    }

    private static SudoVdaPackagePolicy ToPolicy(PolicyDocument value)
    {
        if (value.SchemaVersion != 1
            || !string.Equals(value.Architecture, "x64", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                value.HardwareId,
                @"ROOT\SudoMaker\SudoVDA",
                StringComparison.OrdinalIgnoreCase)
            || !Version.TryParse(value.MinimumProtocolVersion, out Version? protocol)
            || protocol < new Version(0, 2, 0)
            || string.IsNullOrWhiteSpace(value.SignerSubject)
            || string.IsNullOrWhiteSpace(value.SignerThumbprint))
        {
            throw new InvalidDataException("Driver policy is incompatible or incomplete.");
        }
        return new SudoVdaPackagePolicy(
            "x64",
            @"ROOT\SudoMaker\SudoVDA",
            protocol,
            value.SignerSubject,
            NormalizeThumbprint(value.SignerThumbprint));
    }

    private static string NormalizeThumbprint(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();

    private sealed record PolicyDocument(
        int SchemaVersion,
        string Architecture,
        string HardwareId,
        string MinimumProtocolVersion,
        string SignerSubject,
        string SignerThumbprint);
}
