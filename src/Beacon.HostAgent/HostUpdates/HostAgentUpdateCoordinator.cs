using Beacon.HostAgent.Contracts;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.HostUpdates;

internal sealed class HostAgentUpdateCoordinator(
    HostAgentUpdateStorage storage,
    IHostAgentUpdatePackageValidator validator,
    HostAgentUpdateJournal journal,
    HostAgentUpdateStateStore state) : IHostAgentUpdateExecutor
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<HostAgentUpdatePayload> StageAsync(
        string packageId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        string normalizedPackageId = RequirePackageId(packageId);
        if (transactionId == Guid.Empty)
        {
            throw new HostAgentUpdateStartException(
                "invalid-transaction-id",
                "Host Agent update transaction id is required.");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (journal.TryRead(transactionId, out HostAgentUpdatePayload? existing)
                && existing is not null)
            {
                if (!string.Equals(existing.PackageId, normalizedPackageId, StringComparison.Ordinal))
                {
                    throw new HostAgentUpdateStartException(
                        "transaction-package-conflict",
                        "Host Agent update transaction id belongs to another package.");
                }
                return existing;
            }

            HostAgentPendingUpdate? active = state.ReadPending();
            if (active is not null && active.TransactionId != transactionId)
            {
                throw new HostAgentUpdateStartException(
                    "host-agent-update-busy",
                    "Another Host Agent update transaction is pending.");
            }

            var accepted = new HostAgentUpdatePayload(
                transactionId,
                normalizedPackageId,
                HostAgentUpdateState.Accepted,
                "host-agent-update-accepted",
                SourceCommit: string.Empty,
                PreviousVersionId: null,
                ActiveVersionId: null);
            journal.Write(accepted);
            return await StageCoreAsync(accepted).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public bool TryGet(Guid transactionId, out HostAgentUpdatePayload? value) =>
        journal.TryRead(transactionId, out value);

    private async Task<HostAgentUpdatePayload> StageCoreAsync(HostAgentUpdatePayload accepted)
    {
        HostAgentSelectedVersion current = state.ReadCurrent()
            ?? throw new HostAgentUpdateStartException(
                "current-version-unavailable",
                "Current Host Agent version state is unavailable.");
        HostAgentUpdatePayload validating = accepted with
        {
            State = HostAgentUpdateState.Validating,
            Diagnostic = "host-agent-update-validating",
            PreviousVersionId = current.VersionId
        };
        journal.Write(validating);

        string sourceRoot = Path.Combine(storage.Inbox, accepted.PackageId);
        HostAgentValidatedPackage source;
        try
        {
            source = await validator.ValidateAsync(sourceRoot, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (HostAgentUpdateValidationException error)
        {
            throw new HostAgentUpdateStartException(error.Code, error.Message, error);
        }
        if (!string.Equals(
                source.Manifest.PackageId,
                accepted.PackageId,
                StringComparison.Ordinal))
        {
            throw new HostAgentUpdateStartException(
                "package-id-mismatch",
                "Host Agent package id does not match its inbox directory.");
        }

        string finalRoot = Path.Combine(storage.StagedPackages, accepted.TransactionId.ToString("D"));
        string temporaryRoot = $"{finalRoot}.staging";
        if (Directory.Exists(finalRoot) || Directory.Exists(temporaryRoot))
        {
            throw new HostAgentUpdateStartException(
                "package-stage-exists",
                "Host Agent protected staging target already exists.");
        }

        try
        {
            Directory.CreateDirectory(temporaryRoot);
            await CopyPackageAsync(source, temporaryRoot).ConfigureAwait(false);
            _ = await validator.ValidateAsync(temporaryRoot, CancellationToken.None)
                .ConfigureAwait(false);
            Directory.Move(temporaryRoot, finalRoot);

            var pending = new HostAgentPendingUpdate(
                accepted.TransactionId,
                accepted.PackageId,
                source.Manifest.SourceCommit,
                current.VersionId,
                current.SourceCommit,
                finalRoot);
            state.WritePending(pending);
            var staged = validating with
            {
                State = HostAgentUpdateState.Staged,
                Diagnostic = "host-agent-update-staged",
                SourceCommit = source.Manifest.SourceCommit
            };
            journal.Write(staged);
            return staged;
        }
        catch (HostAgentUpdateValidationException error)
        {
            throw new HostAgentUpdateStartException(error.Code, error.Message, error);
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

    private static async Task CopyPackageAsync(
        HostAgentValidatedPackage package,
        string destinationRoot)
    {
        await CopyFileAsync(
            Path.Combine(package.PackageRoot, "manifest.json"),
            Path.Combine(destinationRoot, "manifest.json")).ConfigureAwait(false);
        await CopyFileAsync(
            Path.Combine(package.PackageRoot, "manifest.sig"),
            Path.Combine(destinationRoot, "manifest.sig")).ConfigureAwait(false);
        foreach (HostAgentUpdateFile file in package.Manifest.Files)
        {
            string relative = HostAgentUpdatePackageValidator.NormalizeRelativePath(file.Path);
            string source = Path.Combine(
                package.PackageRoot,
                "payload",
                relative.Replace('/', Path.DirectorySeparatorChar));
            string destination = Path.Combine(
                destinationRoot,
                "payload",
                relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(source, destination).ConfigureAwait(false);
        }
    }

    private static async Task CopyFileAsync(string source, string destination)
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
        await input.CopyToAsync(output, CancellationToken.None).ConfigureAwait(false);
        await output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private static string RequirePackageId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value is "." or "..")
        {
            throw new HostAgentUpdateStartException(
                "invalid-package-id",
                "Host Agent package id is invalid.");
        }
        return value.Trim();
    }
}

internal sealed class HostAgentUpdateStartException : InvalidOperationException
{
    public HostAgentUpdateStartException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    public string Code { get; }
}
