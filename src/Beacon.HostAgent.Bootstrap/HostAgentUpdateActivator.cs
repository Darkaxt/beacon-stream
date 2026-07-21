using System.Security.Cryptography;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Bootstrap;

internal sealed class HostAgentUpdateActivator(
    BootstrapStorage storage,
    IHostAgentUpdatePackageValidator validator) : IHostAgentUpdateActivator
{
    public async Task<HostAgentSelectedVersion> ActivateAsync(
        HostAgentPendingUpdate pending,
        HostAgentSelectedVersion current)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(current);
        string expectedStagedRoot = BootstrapStorage.ResolveChild(
            storage.StagedPackagesRoot,
            pending.TransactionId.ToString("D"));
        if (!string.Equals(
                Path.GetFullPath(pending.StagedPackageRoot),
                expectedStagedRoot,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(pending.PreviousVersionId, current.VersionId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Pending Host Agent update state is inconsistent.");
        }

        HostAgentValidatedPackage package = await validator.ValidateAsync(
            expectedStagedRoot,
            CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(package.Manifest.PackageId, pending.PackageId, StringComparison.Ordinal)
            || !string.Equals(package.Manifest.SourceCommit, pending.SourceCommit, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Protected Host Agent package identity is inconsistent.");
        }

        string finalRoot = storage.GetVersionRoot(pending.PackageId);
        string temporaryRoot = $"{finalRoot}.pending";
        if (Directory.Exists(finalRoot))
        {
            await VerifyInstalledAsync(finalRoot, package).ConfigureAwait(false);
            return new HostAgentSelectedVersion(pending.PackageId, pending.SourceCommit);
        }
        if (Directory.Exists(temporaryRoot))
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }

        try
        {
            Directory.CreateDirectory(temporaryRoot);
            foreach (HostAgentUpdateFile file in package.Manifest.Files)
            {
                string relative = HostAgentUpdatePackageValidator.NormalizeRelativePath(file.Path);
                string source = Path.Combine(
                    package.PackageRoot,
                    "payload",
                    relative.Replace('/', Path.DirectorySeparatorChar));
                string destination = Path.Combine(
                    temporaryRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyFileAsync(source, destination).ConfigureAwait(false);
            }
            await VerifyInstalledAsync(temporaryRoot, package).ConfigureAwait(false);
            Directory.Move(temporaryRoot, finalRoot);
            return new HostAgentSelectedVersion(pending.PackageId, pending.SourceCommit);
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

    private static async Task VerifyInstalledAsync(
        string root,
        HostAgentValidatedPackage package)
    {
        HashSet<string> expected = package.Manifest.Files
            .Select(file => HostAgentUpdatePackageValidator.NormalizeRelativePath(file.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] actual = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToArray();
        if (!expected.SetEquals(actual))
        {
            throw new InvalidDataException("Installed Host Agent file set does not match its manifest.");
        }

        foreach (HostAgentUpdateFile file in package.Manifest.Files)
        {
            string relative = HostAgentUpdatePackageValidator.NormalizeRelativePath(file.Path);
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (new FileInfo(path).Length != file.Length)
            {
                throw new InvalidDataException("Installed Host Agent file length does not match.");
            }
            await using FileStream stream = File.OpenRead(path);
            string hash = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, CancellationToken.None).ConfigureAwait(false));
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Installed Host Agent file hash does not match.");
            }
        }
    }

    private static async Task CopyFileAsync(string source, string destination)
    {
        await using FileStream input = File.OpenRead(source);
        await using FileStream output = new(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 131_072,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, CancellationToken.None).ConfigureAwait(false);
        await output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }
}
