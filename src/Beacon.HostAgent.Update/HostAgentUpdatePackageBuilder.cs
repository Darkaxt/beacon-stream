using System.Security.Cryptography;
using System.Text.Json;

namespace Beacon.HostAgent.Update;

public static class HostAgentUpdatePackageBuilder
{
    public static async Task<HostAgentBuiltPackage> BuildAsync(
        string payloadRoot,
        string packageRoot,
        HostAgentUpdateBuildIdentity identity,
        string privateKeyPem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            throw new ArgumentException("Host Agent update private key is required.", nameof(privateKeyPem));
        }

        string source = RequirePayloadRoot(payloadRoot);
        string destination = RequireNewPackageRoot(packageRoot);
        var inspector = new WindowsHostAgentPackageEntryInspector();
        IReadOnlyList<string> sourceFiles = EnumerateSourceFiles(source, inspector);
        if (sourceFiles.Count == 0)
        {
            throw new InvalidDataException("Host Agent update payload cannot be empty.");
        }

        try
        {
            string payloadDestination = Path.Combine(destination, "payload");
            Directory.CreateDirectory(payloadDestination);
            var files = new List<HostAgentUpdateFile>(sourceFiles.Count);
            foreach (string sourcePath in sourceFiles
                .OrderBy(path => Path.GetRelativePath(source, path), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(source, sourcePath).Replace('\\', '/');
                relativePath = HostAgentUpdatePackageValidator.NormalizeRelativePath(relativePath);
                string destinationPath = Path.Combine(
                    payloadDestination,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await CopyFileAsync(sourcePath, destinationPath, cancellationToken).ConfigureAwait(false);
                files.Add(new HostAgentUpdateFile(
                    relativePath,
                    new FileInfo(destinationPath).Length,
                    await HashFileAsync(destinationPath, cancellationToken).ConfigureAwait(false)));
            }

            var manifest = new HostAgentUpdateManifest(
                HostAgentUpdatePackageValidator.ManifestSchemaVersion,
                identity.PackageId,
                identity.SourceCommit,
                HostAgentUpdatePackageValidator.ProductTarget,
                HostAgentUpdatePackageValidator.ProductArchitecture,
                identity.MinimumBootstrapVersion,
                HostAgentUpdatePackageValidator.RequiredEntryPoint,
                files);
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                HostAgentUpdateJson.Options);
            await File.WriteAllBytesAsync(
                Path.Combine(destination, "manifest.json"),
                manifestBytes,
                cancellationToken).ConfigureAwait(false);

            byte[] signature;
            using (ECDsa key = ECDsa.Create())
            {
                key.ImportFromPem(privateKeyPem);
                signature = key.SignData(manifestBytes, HashAlgorithmName.SHA256);
            }
            await File.WriteAllBytesAsync(
                Path.Combine(destination, "manifest.sig"),
                signature,
                cancellationToken).ConfigureAwait(false);
            return new HostAgentBuiltPackage(destination, manifest, manifestBytes);
        }
        catch
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }
            throw;
        }
    }

    private static IReadOnlyList<string> EnumerateSourceFiles(
        string root,
        IHostAgentPackageEntryInspector inspector)
    {
        var files = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out string? directory))
        {
            RejectUnsafe(directory, inspector);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectUnsafe(entry, inspector);
                if (Directory.Exists(entry))
                {
                    pending.Enqueue(entry);
                }
                else
                {
                    files.Add(entry);
                }
            }
        }
        return files;
    }

    private static void RejectUnsafe(string path, IHostAgentPackageEntryInspector inspector)
    {
        if (inspector.IsReparsePoint(path))
        {
            throw HostAgentUpdatePackageValidator.Reject(
                "package-reparse-point",
                "Host Agent update payload cannot contain reparse points.");
        }
        if (inspector.HasAlternateDataStream(path))
        {
            throw HostAgentUpdatePackageValidator.Reject(
                "package-alternate-data-stream",
                "Host Agent update payload cannot contain alternate data streams.");
        }
    }

    private static string RequirePayloadRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A fully qualified payload root is required.", nameof(path));
        }
        string root = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Directory.Exists(root)
            ? root
            : throw new DirectoryNotFoundException("Host Agent update payload root does not exist.");
    }

    private static string RequireNewPackageRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A fully qualified package root is required.", nameof(path));
        }
        string root = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Directory.Exists(root) || File.Exists(root))
        {
            throw new IOException("Host Agent update package root already exists.");
        }
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using FileStream input = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 131_072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream output = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 131_072,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
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
}
