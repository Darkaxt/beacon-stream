using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beacon.HostAgent.Update;

public sealed partial class HostAgentUpdatePackageValidator
{
    public const int ManifestSchemaVersion = 1;
    public const string ProductTarget = "beacon-host-agent";
    public const string ProductArchitecture = "win-x64";
    public const string RequiredEntryPoint = "Beacon.HostAgent.exe";

    private const string ManifestFileName = "manifest.json";
    private const string SignatureFileName = "manifest.sig";
    private readonly string publicKeyPem;
    private readonly Version bootstrapVersion;
    private readonly IHostAgentPackageEntryInspector inspector;

    public HostAgentUpdatePackageValidator(
        string publicKeyPem,
        Version bootstrapVersion,
        IHostAgentPackageEntryInspector? inspector = null)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            throw new ArgumentException("Host Agent update public key is required.", nameof(publicKeyPem));
        }
        this.publicKeyPem = publicKeyPem;
        this.bootstrapVersion = bootstrapVersion
            ?? throw new ArgumentNullException(nameof(bootstrapVersion));
        this.inspector = inspector ?? new WindowsHostAgentPackageEntryInspector();
    }

    public async Task<HostAgentValidatedPackage> ValidateAsync(
        string packageRoot,
        CancellationToken cancellationToken)
    {
        string root = RequireExistingRoot(packageRoot);
        IReadOnlyList<string> tree = EnumerateSafeTree(root);
        string manifestPath = Path.Combine(root, ManifestFileName);
        string signaturePath = Path.Combine(root, SignatureFileName);
        if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
        {
            throw Reject("invalid-package-shape", "Host Agent package manifest or signature is missing.");
        }

        byte[] manifestBytes;
        byte[] signature;
        try
        {
            manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken)
                .ConfigureAwait(false);
            signature = await File.ReadAllBytesAsync(signaturePath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException error)
        {
            throw Reject("invalid-package-shape", "Host Agent package metadata cannot be read.", error);
        }

        HostAgentUpdateManifest manifest = ReadManifest(manifestBytes);
        ValidateManifestIdentity(manifest);
        VerifySignature(manifestBytes, signature);

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (HostAgentUpdateFile file in manifest.Files)
        {
            string relativePath = NormalizeRelativePath(file.Path);
            if (!declared.Add(relativePath))
            {
                throw Reject("duplicate-package-file", "Host Agent package contains duplicate paths.");
            }
            if (file.Length < 0)
            {
                throw Reject("invalid-package-manifest", "Host Agent package file length is invalid.");
            }
            if (!Sha256Pattern().IsMatch(file.Sha256))
            {
                throw Reject("invalid-package-manifest", "Host Agent package digest is invalid.");
            }

            string fullPath = ResolvePayloadPath(root, relativePath);
            if (!File.Exists(fullPath))
            {
                throw Reject("unexpected-package-file", "A declared Host Agent package file is missing.");
            }
            if (new FileInfo(fullPath).Length != file.Length)
            {
                throw Reject("package-length-mismatch", "A Host Agent package file length does not match.");
            }

            string hash = await HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Reject("package-hash-mismatch", "A Host Agent package file digest does not match.");
            }
            fileHashes[relativePath] = hash;
        }

        string entryPoint = NormalizeRelativePath(manifest.EntryPoint);
        if (!string.Equals(entryPoint, RequiredEntryPoint, StringComparison.Ordinal)
            || !declared.Contains(entryPoint))
        {
            throw Reject(
                "package-entry-point-mismatch",
                "Host Agent package entry point is missing or incompatible.");
        }

        HashSet<string> actual = tree
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => path.StartsWith("payload/", StringComparison.OrdinalIgnoreCase))
            .Select(path => path["payload/".Length..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!declared.SetEquals(actual))
        {
            throw Reject(
                "unexpected-package-file",
                "Host Agent package contains an undeclared or missing payload file.");
        }

        HashSet<string> allowedPackageFiles = declared
            .Select(path => $"payload/{path}")
            .Append(ManifestFileName)
            .Append(SignatureFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] allFiles = tree
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToArray();
        if (!allowedPackageFiles.SetEquals(allFiles))
        {
            throw Reject("unexpected-package-file", "Host Agent package contains an unexpected file.");
        }

        return new HostAgentValidatedPackage(root, manifest, manifestBytes, fileHashes);
    }

    public static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathFullyQualified(path)
            || path.StartsWith("/", StringComparison.Ordinal)
            || path.Contains("\\", StringComparison.Ordinal))
        {
            throw Reject("invalid-package-path", "Host Agent package path is invalid.");
        }

        string[] segments = path.Split('/');
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.EndsWith(" ", StringComparison.Ordinal)
                || segment.EndsWith(".", StringComparison.Ordinal)))
        {
            throw Reject("invalid-package-path", "Host Agent package path is invalid.");
        }
        return string.Join('/', segments);
    }

    internal static HostAgentUpdateValidationException Reject(
        string code,
        string message,
        Exception? inner = null) =>
        inner is null
            ? new HostAgentUpdateValidationException(code, message)
            : new HostAgentUpdateValidationException(code, $"{message} {inner.Message}");

    private IReadOnlyList<string> EnumerateSafeTree(string root)
    {
        RejectUnsafeEntry(root);
        var found = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectUnsafeEntry(entry);
                found.Add(entry);
                if (Directory.Exists(entry))
                {
                    pending.Enqueue(entry);
                }
            }
        }
        return found;
    }

    private void RejectUnsafeEntry(string path)
    {
        if (inspector.IsReparsePoint(path))
        {
            throw Reject("package-reparse-point", "Host Agent packages cannot contain reparse points.");
        }
        if (inspector.HasAlternateDataStream(path))
        {
            throw Reject(
                "package-alternate-data-stream",
                "Host Agent packages cannot contain alternate data streams.");
        }
    }

    private static string RequireExistingRoot(string packageRoot)
    {
        if (string.IsNullOrWhiteSpace(packageRoot) || !Path.IsPathFullyQualified(packageRoot))
        {
            throw Reject("invalid-package-root", "A fully qualified Host Agent package root is required.");
        }
        string root = Path.GetFullPath(packageRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Directory.Exists(root)
            ? root
            : throw Reject("package-not-found", "Host Agent package does not exist.");
    }

    private static HostAgentUpdateManifest ReadManifest(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<HostAgentUpdateManifest>(bytes, HostAgentUpdateJson.Options)
                ?? throw Reject("invalid-package-manifest", "Host Agent package manifest is empty.");
        }
        catch (HostAgentUpdateValidationException)
        {
            throw;
        }
        catch (JsonException error)
        {
            throw Reject("invalid-package-manifest", "Host Agent package manifest is invalid.", error);
        }
    }

    private void ValidateManifestIdentity(HostAgentUpdateManifest manifest)
    {
        if (manifest.SchemaVersion != ManifestSchemaVersion
            || !PackageIdPattern().IsMatch(manifest.PackageId)
            || !CommitPattern().IsMatch(manifest.SourceCommit)
            || manifest.Files is null
            || manifest.Files.Count == 0)
        {
            throw Reject("invalid-package-manifest", "Host Agent package manifest is incomplete.");
        }
        if (!string.Equals(manifest.Target, ProductTarget, StringComparison.Ordinal))
        {
            throw Reject("package-target-mismatch", "Host Agent package target is incompatible.");
        }
        if (!string.Equals(manifest.Architecture, ProductArchitecture, StringComparison.Ordinal))
        {
            throw Reject(
                "package-architecture-mismatch",
                "Host Agent package architecture is incompatible.");
        }
        if (!Version.TryParse(manifest.MinimumBootstrapVersion, out Version? minimum)
            || minimum > bootstrapVersion)
        {
            throw Reject(
                "package-bootstrap-incompatible",
                "Host Agent package requires a newer bootstrap.");
        }
    }

    private void VerifySignature(byte[] manifest, byte[] signature)
    {
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            if (!key.VerifyData(manifest, signature, HashAlgorithmName.SHA256))
            {
                throw Reject("package-signature-invalid", "Host Agent package signature is invalid.");
            }
        }
        catch (HostAgentUpdateValidationException)
        {
            throw;
        }
        catch (CryptographicException error)
        {
            throw Reject("package-signature-invalid", "Host Agent package signature is invalid.", error);
        }
    }

    private static string ResolvePayloadPath(string root, string relativePath)
    {
        string payloadRoot = Path.Combine(root, "payload");
        string fullPayloadRoot = Path.GetFullPath(payloadRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(
            fullPayloadRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = $"{fullPayloadRoot}{Path.DirectorySeparatorChar}";
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw Reject("invalid-package-path", "Host Agent package path escapes its payload root.");
        }
        return candidate;
    }

    private static async Task<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 131_072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex("^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
