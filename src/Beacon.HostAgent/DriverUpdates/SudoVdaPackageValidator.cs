using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace Beacon.HostAgent.DriverUpdates;

internal sealed class SudoVdaPackageValidator(
    SudoVdaPackagePaths paths,
    SudoVdaPackagePolicy policy,
    ISudoVdaSignatureVerifier signatures)
{
    private const int ManifestSchemaVersion = 1;
    private const string ManifestFileName = "manifest.json";
    private static readonly JsonSerializerOptions ManifestJson = CreateManifestJson();

    public async Task<SudoVdaValidatedPackage> ValidateAndStageAsync(
        string packageId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        string normalizedPackageId = ValidatePackageId(packageId);
        if (transactionId == Guid.Empty)
        {
            throw Reject("invalid-transaction-id", "Driver update transaction id is required.");
        }

        string inbox = CanonicalDirectory(paths.Inbox);
        string staged = CanonicalDirectory(paths.Staged);
        string sourceRoot = ResolveChild(inbox, normalizedPackageId);
        if (!Directory.Exists(sourceRoot))
        {
            throw Reject("package-not-found", "The staged driver package does not exist.");
        }

        SudoVdaValidatedPackage source = await ValidateRootAsync(
            normalizedPackageId,
            sourceRoot,
            cancellationToken).ConfigureAwait(false);

        string finalRoot = ResolveChild(staged, transactionId.ToString("D"));
        string temporaryRoot = $"{finalRoot}.staging";
        if (Directory.Exists(finalRoot) || Directory.Exists(temporaryRoot))
        {
            throw Reject("package-stage-exists", "The driver update staging target already exists.");
        }

        try
        {
            Directory.CreateDirectory(temporaryRoot);
            foreach (SudoVdaPackageFile file in ReadManifest(sourceRoot).Files)
            {
                string sourcePath = ResolveManifestFile(sourceRoot, file.Path);
                string destinationPath = ResolveManifestFile(temporaryRoot, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await using FileStream input = new(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 131_072,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using FileStream output = new(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 131_072,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Copy(
                Path.Combine(sourceRoot, ManifestFileName),
                Path.Combine(temporaryRoot, ManifestFileName),
                overwrite: false);
            _ = await ValidateRootAsync(
                normalizedPackageId,
                temporaryRoot,
                cancellationToken).ConfigureAwait(false);
            Directory.Move(temporaryRoot, finalRoot);
            return source with
            {
                PackageRoot = finalRoot,
                InfPath = ResolveManifestFile(finalRoot, Path.GetRelativePath(sourceRoot, source.InfPath))
            };
        }
        catch
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
            throw;
        }
    }

    private async Task<SudoVdaValidatedPackage> ValidateRootAsync(
        string packageId,
        string packageRoot,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> tree = EnumerateSafeTree(packageRoot);
        SudoVdaPackageManifest manifest = ReadManifest(packageRoot);
        ValidateManifest(manifest);

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ManifestFileName
        };
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (SudoVdaPackageFile file in manifest.Files)
        {
            string relativePath = NormalizeManifestPath(file.Path);
            if (!declared.Add(relativePath))
            {
                throw Reject("duplicate-package-file", "The package manifest contains a duplicate file.");
            }

            string fullPath = ResolveManifestFile(packageRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                throw Reject("package-file-missing", "A declared package file is missing.");
            }

            string actualHash = await HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (!IsSha256(file.Sha256)
                || !string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Reject("package-hash-mismatch", "A package file digest does not match its manifest.");
            }
            hashes[relativePath] = actualHash;
        }

        string[] actualFiles = tree
            .Where(File.Exists)
            .Select(path => Path.GetRelativePath(packageRoot, path))
            .ToArray();
        if (!declared.SetEquals(actualFiles))
        {
            throw Reject("unexpected-package-file", "The package contains an undeclared or missing file.");
        }

        string infRelativePath = NormalizeManifestPath(manifest.InfPath);
        if (!declared.Contains(infRelativePath))
        {
            throw Reject("package-inf-mismatch", "The package INF is not present in the declared file set.");
        }

        string infPath = ResolveManifestFile(packageRoot, infRelativePath);
        InfEvidence inf = ParseAndValidateInf(infPath, manifest);
        ValidateSignatures(packageRoot, manifest, inf);

        return new SudoVdaValidatedPackage(
            packageId,
            manifest.PackageVersion,
            manifest.ProtocolVersion,
            manifest.HardwareId,
            packageRoot,
            infPath,
            manifest.SignerSubject,
            manifest.SignerThumbprint,
            hashes);
    }

    private void ValidateManifest(SudoVdaPackageManifest manifest)
    {
        if (manifest.SchemaVersion != ManifestSchemaVersion
            || string.IsNullOrWhiteSpace(manifest.PackageVersion)
            || manifest.Files is null
            || manifest.Files.Count == 0)
        {
            throw Reject("invalid-package-manifest", "The package manifest is incomplete or unsupported.");
        }

        if (!string.Equals(manifest.Architecture, policy.Architecture, StringComparison.OrdinalIgnoreCase))
        {
            throw Reject("package-architecture-mismatch", "The driver package architecture is incompatible.");
        }

        if (!string.Equals(manifest.HardwareId, policy.HardwareId, StringComparison.OrdinalIgnoreCase))
        {
            throw Reject("package-hardware-id-mismatch", "The driver package hardware identity is incompatible.");
        }

        if (!Version.TryParse(manifest.ProtocolVersion, out Version? protocol)
            || protocol < policy.MinimumProtocolVersion)
        {
            throw Reject("package-protocol-incompatible", "The driver package protocol is incompatible.");
        }

        if (!string.Equals(manifest.SignerSubject, policy.SignerSubject, StringComparison.Ordinal)
            || !string.Equals(
                NormalizeThumbprint(manifest.SignerThumbprint),
                NormalizeThumbprint(policy.SignerThumbprint),
                StringComparison.Ordinal))
        {
            throw Reject("package-signer-mismatch", "The driver package signer is not allowed.");
        }
    }

    private InfEvidence ParseAndValidateInf(string infPath, SudoVdaPackageManifest manifest)
    {
        InfDocument inf;
        try
        {
            inf = InfDocument.Parse(File.ReadAllLines(infPath));
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            throw Reject("package-inf-mismatch", "The driver package INF cannot be read.");
        }

        string provider = inf.ResolveString(inf.RequiredValue("Version", "Provider"));
        string catalog = Unquote(inf.RequiredValue("Version", "CatalogFile"));
        string driverVersion = ParseDriverVersion(inf.RequiredValue("Version", "DriverVer"));
        bool classMatches = string.Equals(
            Unquote(inf.RequiredValue("Version", "Class")),
            "Display",
            StringComparison.OrdinalIgnoreCase);
        bool providerMatches = string.Equals(provider, "SudoMaker", StringComparison.Ordinal);
        bool architectureMatches = inf.SectionValues("Manufacturer")
            .Any(value => value.Split(',').Any(part =>
                string.Equals(part.Trim(), "NTamd64", StringComparison.OrdinalIgnoreCase)));
        bool hardwareMatches = inf.SectionEntries("Standard.NTamd64")
            .SelectMany(entry => entry.Value.Split(',').Skip(1))
            .Select(value => Unquote(value.Trim()))
            .Any(value => string.Equals(value, policy.HardwareId, StringComparison.OrdinalIgnoreCase));
        string[] binaries = inf.SectionEntries("SourceDisksFiles")
            .Select(entry => Unquote(entry.Key.Trim()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (!classMatches
            || !providerMatches
            || !architectureMatches
            || !hardwareMatches
            || !string.Equals(driverVersion, manifest.PackageVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(catalog)
            || binaries.Length == 0)
        {
            throw Reject("package-inf-mismatch", "The INF does not describe the required SudoVDA display package.");
        }

        var expectedPackageFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeManifestPath(manifest.InfPath),
            NormalizeManifestPath(catalog)
        };
        foreach (string binary in binaries)
        {
            expectedPackageFiles.Add(NormalizeManifestPath(binary));
        }
        var declaredPackageFiles = manifest.Files
            .Select(file => NormalizeManifestPath(file.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expectedPackageFiles.SetEquals(declaredPackageFiles))
        {
            throw Reject("package-inf-mismatch", "The manifest file set does not match the INF package.");
        }

        return new InfEvidence(catalog, binaries);
    }

    private void ValidateSignatures(
        string packageRoot,
        SudoVdaPackageManifest manifest,
        InfEvidence inf)
    {
        foreach (string relativePath in new[] { inf.Catalog }.Concat(inf.Binaries))
        {
            SudoVdaSignatureEvidence signature = signatures.Verify(
                ResolveManifestFile(packageRoot, relativePath));
            if (!signature.Valid)
            {
                throw Reject("package-signature-invalid", "A package signature is invalid.");
            }

            if (!string.Equals(signature.Subject, manifest.SignerSubject, StringComparison.Ordinal)
                || !string.Equals(
                    NormalizeThumbprint(signature.Thumbprint),
                    NormalizeThumbprint(manifest.SignerThumbprint),
                    StringComparison.Ordinal))
            {
                throw Reject("package-signer-mismatch", "A package file signer is not allowed.");
            }
        }
    }

    private static IReadOnlyList<string> EnumerateSafeTree(string root)
    {
        RejectReparseOrStreams(root);
        var found = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            string directory = pending.Dequeue();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectReparseOrStreams(entry);
                found.Add(entry);
                if (Directory.Exists(entry))
                {
                    pending.Enqueue(entry);
                }
            }
        }
        return found;
    }

    private static void RejectReparseOrStreams(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw Reject("package-reparse-point", "Driver packages cannot contain reparse points.");
        }

        if (OperatingSystem.IsWindows() && WindowsAlternateDataStreams.HasAlternateStream(path))
        {
            throw Reject(
                "package-alternate-data-stream",
                "Driver packages cannot contain alternate data streams.");
        }
    }

    private static SudoVdaPackageManifest ReadManifest(string root)
    {
        string path = Path.Combine(root, ManifestFileName);
        try
        {
            return JsonSerializer.Deserialize<SudoVdaPackageManifest>(
                File.ReadAllBytes(path),
                ManifestJson)
                ?? throw Reject("invalid-package-manifest", "The package manifest is empty.");
        }
        catch (SudoVdaPackageValidationException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            throw Reject("invalid-package-manifest", "The package manifest is invalid.");
        }
    }

    private static string ValidatePackageId(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId)
            || packageId.Length > 128
            || !char.IsAsciiLetterOrDigit(packageId[0])
            || packageId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character)
                  || character is '.' or '_' or '-')))
        {
            throw Reject("invalid-package-id", "Driver package id is invalid.");
        }
        return packageId;
    }

    private static string NormalizeManifestPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || path.Contains(':', StringComparison.Ordinal))
        {
            throw Reject("invalid-package-file-path", "A package file path is invalid.");
        }

        string[] segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(segment => segment is "." or ".." || segment.Trim() != segment))
        {
            throw Reject("invalid-package-file-path", "A package file path is invalid.");
        }
        return Path.Combine(segments);
    }

    private static string ResolveManifestFile(string root, string relativePath) =>
        ResolveChild(root, NormalizeManifestPath(relativePath));

    private static string ResolveChild(string root, string child)
    {
        string canonicalRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(canonicalRoot, child));
        string prefix = canonicalRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw Reject("invalid-package-file-path", "A package path escapes its protected root.");
        }
        return candidate;
    }

    private static string CanonicalDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A Host Agent package root is required.", nameof(path));
        }
        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
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
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false));
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 }
        && value.All(character => char.IsAsciiHexDigit(character));

    private static string ParseDriverVersion(string value)
    {
        string[] parts = value.Split(',', 2);
        return parts.Length == 2 ? Unquote(parts[1].Trim()) : string.Empty;
    }

    private static string NormalizeThumbprint(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private static SudoVdaPackageValidationException Reject(string code, string message) =>
        new(code, message);

    private static JsonSerializerOptions CreateManifestJson() => new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private sealed record InfEvidence(string Catalog, IReadOnlyList<string> Binaries);

    private sealed class InfDocument
    {
        private readonly Dictionary<string, List<KeyValuePair<string, string>>> sections;

        private InfDocument(Dictionary<string, List<KeyValuePair<string, string>>> sections)
        {
            this.sections = sections;
        }

        public static InfDocument Parse(IEnumerable<string> lines)
        {
            var sections = new Dictionary<string, List<KeyValuePair<string, string>>>(
                StringComparer.OrdinalIgnoreCase);
            List<KeyValuePair<string, string>>? current = null;
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(';'))
                {
                    continue;
                }
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    string sectionName = line[1..^1].Trim();
                    if (!sections.TryGetValue(sectionName, out current))
                    {
                        current = [];
                        sections.Add(sectionName, current);
                    }
                    continue;
                }
                if (current is null)
                {
                    throw new InvalidDataException("INF entry appears before a section.");
                }
                int equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }
                current.Add(new KeyValuePair<string, string>(
                    line[..equals].Trim(),
                    StripComment(line[(equals + 1)..]).Trim()));
            }
            return new InfDocument(sections);
        }

        public string RequiredValue(string section, string key) =>
            SectionEntries(section)
                .FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                .Value
            ?? throw new InvalidDataException($"INF value {section}/{key} is missing.");

        public IEnumerable<string> SectionValues(string section) =>
            SectionEntries(section).Select(entry => entry.Value);

        public IReadOnlyList<KeyValuePair<string, string>> SectionEntries(string section) =>
            sections.TryGetValue(section, out List<KeyValuePair<string, string>>? values)
                ? values
                : throw new InvalidDataException($"INF section {section} is missing.");

        public string ResolveString(string value)
        {
            string normalized = Unquote(value);
            if (normalized.Length < 3 || normalized[0] != '%' || normalized[^1] != '%')
            {
                return normalized;
            }
            return Unquote(RequiredValue("Strings", normalized[1..^1]));
        }

        private static string StripComment(string value)
        {
            bool quoted = false;
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] == '"')
                {
                    quoted = !quoted;
                }
                else if (value[index] == ';' && !quoted)
                {
                    return value[..index];
                }
            }
            return value;
        }
    }

    private static class WindowsAlternateDataStreams
    {
        private static readonly nint InvalidHandleValue = new(-1);

        public static bool HasAlternateStream(string path)
        {
            nint handle = FindFirstStreamW(path, 0, out FindStreamData data, 0);
            if (handle == InvalidHandleValue)
            {
                int error = Marshal.GetLastWin32Error();
                if (error is 38 or 2)
                {
                    return false;
                }
                throw new Win32Exception(error, "Unable to inspect package data streams.");
            }

            try
            {
                do
                {
                    if (!string.Equals(data.StreamName, "::$DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                while (FindNextStreamW(handle, out data));

                int error = Marshal.GetLastWin32Error();
                if (error != 38)
                {
                    throw new Win32Exception(error, "Unable to enumerate package data streams.");
                }
                return false;
            }
            finally
            {
                _ = FindClose(handle);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint FindFirstStreamW(
            string lpFileName,
            int infoLevel,
            out FindStreamData lpFindStreamData,
            int dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextStreamW(
            nint hFindStream,
            out FindStreamData lpFindStreamData);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindClose(nint hFindFile);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FindStreamData
        {
            public long StreamSize;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
            public string StreamName;
        }
    }
}
