using Beacon.HostAgent.Contracts;

namespace Beacon.HostAgent.DriverUpdates;

internal sealed class SudoVdaUpdateCoordinator(
    ISudoVdaPackageValidator validator,
    SudoVdaUpdateJournal journal,
    ISudoVdaUpdatePlatform platform,
    string installedEvidenceRoot) : IHostAgentDriverUpdateExecutor
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, Task> operations = [];
    private Guid? activeTransaction;
    private readonly string evidenceRoot = PrepareEvidenceRoot(installedEvidenceRoot);

    public SudoVdaUpdatePayload Start(
        string packageId,
        Guid transactionId,
        int reportedActiveLeaseCount)
    {
        string normalizedPackageId = RequirePackageId(packageId);
        if (transactionId == Guid.Empty)
        {
            throw new SudoVdaUpdateStartException(
                "invalid-transaction-id",
                "Driver update transaction id is required.");
        }
        if (reportedActiveLeaseCount < 0)
        {
            throw new SudoVdaUpdateStartException(
                "invalid-lease-count",
                "Reported active lease count cannot be negative.");
        }
        if (reportedActiveLeaseCount != 0)
        {
            throw new SudoVdaUpdateStartException(
                "service-active-leases",
                "Beacon Service reports active display leases.");
        }

        lock (gate)
        {
            if (journal.TryRead(transactionId, out SudoVdaUpdatePayload? existing)
                && existing is not null)
            {
                if (!string.Equals(existing.PackageId, normalizedPackageId, StringComparison.Ordinal))
                {
                    throw new SudoVdaUpdateStartException(
                        "transaction-package-conflict",
                        "Driver update transaction id belongs to another package.");
                }
                return existing;
            }

            if (activeTransaction is Guid active
                && operations.TryGetValue(active, out Task? activeOperation)
                && !activeOperation.IsCompleted)
            {
                throw new SudoVdaUpdateStartException(
                    "driver-update-busy",
                    "Another driver update transaction is active.");
            }

            var accepted = new SudoVdaUpdatePayload(
                transactionId,
                normalizedPackageId,
                SudoVdaUpdateState.Accepted,
                "driver-update-accepted",
                PreviousEvidence: null,
                ActiveEvidence: null);
            journal.Write(accepted);
            activeTransaction = transactionId;
            operations[transactionId] = Task.Run(
                () => ExecuteAsync(accepted),
                CancellationToken.None);
            return accepted;
        }
    }

    public bool TryGet(Guid transactionId, out SudoVdaUpdatePayload? value) =>
        journal.TryRead(transactionId, out value);

    public async Task<SudoVdaUpdatePayload> WaitForCompletionAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        Task? operation;
        lock (gate)
        {
            operations.TryGetValue(transactionId, out operation);
        }
        if (operation is not null)
        {
            await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        if (!journal.TryRead(transactionId, out SudoVdaUpdatePayload? result) || result is null)
        {
            throw new KeyNotFoundException("Driver update transaction was not found.");
        }
        return result;
    }

    private async Task ExecuteAsync(SudoVdaUpdatePayload accepted)
    {
        SudoVdaDriverEvidence? previous = null;
        string? exportedPackageRoot = null;
        bool mutationStarted = false;
        try
        {
            Write(accepted with
            {
                State = SudoVdaUpdateState.Validating,
                Diagnostic = "driver-update-validating"
            });

            SudoVdaUpdateHostState initialState = await platform.QueryHostStateAsync()
                .ConfigureAwait(false);
            string? initialGate = GetHostGateFailure(initialState);
            if (initialGate is not null)
            {
                Reject(accepted, initialGate);
                return;
            }

            SudoVdaValidatedPackage package = await validator.ValidateAndStageAsync(
                accepted.PackageId,
                accepted.TransactionId,
                CancellationToken.None).ConfigureAwait(false);
            previous = await platform.QueryActiveEvidenceAsync().ConfigureAwait(false);
            if (!previous.DeviceHealthy)
            {
                Reject(accepted, "active-driver-unhealthy");
                return;
            }

            exportedPackageRoot = Path.Combine(
                evidenceRoot,
                accepted.TransactionId.ToString("D"),
                "previous");
            await platform.ExportActivePackageAsync(previous, exportedPackageRoot)
                .ConfigureAwait(false);

            SudoVdaUpdateHostState finalState = await platform.QueryHostStateAsync()
                .ConfigureAwait(false);
            string? finalGate = GetHostGateFailure(finalState);
            if (finalGate is not null)
            {
                Reject(accepted with { PreviousEvidence = previous.ToPayload() }, finalGate);
                return;
            }

            Write(accepted with
            {
                State = SudoVdaUpdateState.Installing,
                Diagnostic = "driver-update-installing",
                PreviousEvidence = previous.ToPayload()
            });
            mutationStarted = true;
            await platform.InstallAsync(package).ConfigureAwait(false);
            await platform.RestartAsync(previous.DeviceInstanceId).ConfigureAwait(false);

            Write(accepted with
            {
                State = SudoVdaUpdateState.Verifying,
                Diagnostic = "driver-update-verifying",
                PreviousEvidence = previous.ToPayload()
            });
            SudoVdaDriverEvidence active = await platform.QueryActiveEvidenceAsync()
                .ConfigureAwait(false);
            if (!MatchesCandidate(active, package))
            {
                await RollbackAsync(accepted, previous, active, exportedPackageRoot)
                    .ConfigureAwait(false);
                return;
            }

            Write(accepted with
            {
                State = SudoVdaUpdateState.Succeeded,
                Diagnostic = "driver-update-succeeded",
                PreviousEvidence = previous.ToPayload(),
                ActiveEvidence = active.ToPayload()
            });
        }
        catch (SudoVdaPackageValidationException error)
        {
            Reject(accepted, error.Code);
        }
        catch (Exception)
        {
            if (mutationStarted && previous is not null && exportedPackageRoot is not null)
            {
                await RollbackAsync(accepted, previous, active: null, exportedPackageRoot)
                    .ConfigureAwait(false);
            }
            else
            {
                Reject(accepted, "driver-update-preparation-failed");
            }
        }
        finally
        {
            lock (gate)
            {
                if (activeTransaction == accepted.TransactionId)
                {
                    activeTransaction = null;
                }
            }
        }
    }

    private async Task RollbackAsync(
        SudoVdaUpdatePayload accepted,
        SudoVdaDriverEvidence previous,
        SudoVdaDriverEvidence? active,
        string exportedPackageRoot)
    {
        Write(accepted with
        {
            State = SudoVdaUpdateState.RollingBack,
            Diagnostic = "driver-update-rolling-back",
            PreviousEvidence = previous.ToPayload(),
            ActiveEvidence = active?.ToPayload()
        });
        try
        {
            await platform.RollbackAsync(exportedPackageRoot, previous).ConfigureAwait(false);
            await platform.RestartAsync(previous.DeviceInstanceId).ConfigureAwait(false);
            SudoVdaDriverEvidence restored = await platform.QueryActiveEvidenceAsync()
                .ConfigureAwait(false);
            bool verified = MatchesPrevious(restored, previous);
            Write(accepted with
            {
                State = verified ? SudoVdaUpdateState.RolledBack : SudoVdaUpdateState.Degraded,
                Diagnostic = verified
                    ? "driver-update-rolled-back"
                    : "rollback-verification-failed",
                PreviousEvidence = previous.ToPayload(),
                ActiveEvidence = restored.ToPayload()
            });
        }
        catch (Exception)
        {
            Write(accepted with
            {
                State = SudoVdaUpdateState.Degraded,
                Diagnostic = "rollback-operation-failed",
                PreviousEvidence = previous.ToPayload(),
                ActiveEvidence = active?.ToPayload()
            });
        }
    }

    private void Reject(SudoVdaUpdatePayload value, string diagnostic) =>
        Write(value with
        {
            State = SudoVdaUpdateState.Rejected,
            Diagnostic = diagnostic
        });

    private void Write(SudoVdaUpdatePayload value) => journal.Write(value);

    private static string? GetHostGateFailure(SudoVdaUpdateHostState state)
    {
        if (state.ActiveLeaseCount != 0)
        {
            return "agent-active-leases";
        }
        return state.ActiveVirtualDisplayCount != 0
            ? "agent-active-virtual-display"
            : null;
    }

    private static bool MatchesCandidate(
        SudoVdaDriverEvidence active,
        SudoVdaValidatedPackage package) =>
        active.DeviceHealthy
        && string.Equals(active.HardwareId, package.HardwareId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(active.DriverVersion, package.PackageVersion, StringComparison.Ordinal)
        && string.Equals(active.ProtocolVersion, package.ProtocolVersion, StringComparison.Ordinal)
        && string.Equals(active.SignerSubject, package.SignerSubject, StringComparison.Ordinal)
        && string.Equals(
            NormalizeThumbprint(active.SignerThumbprint),
            NormalizeThumbprint(package.SignerThumbprint),
            StringComparison.Ordinal)
        && package.FileHashes.TryGetValue("SudoVDA.dll", out string? expectedHash)
        && string.Equals(active.BinarySha256, expectedHash, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(active.PublishedInf);

    private static bool MatchesPrevious(
        SudoVdaDriverEvidence active,
        SudoVdaDriverEvidence previous) =>
        active.DeviceHealthy
        && string.Equals(active.DeviceInstanceId, previous.DeviceInstanceId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(active.HardwareId, previous.HardwareId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(active.PublishedInf, previous.PublishedInf, StringComparison.OrdinalIgnoreCase)
        && string.Equals(active.DriverVersion, previous.DriverVersion, StringComparison.Ordinal)
        && string.Equals(active.ProtocolVersion, previous.ProtocolVersion, StringComparison.Ordinal)
        && string.Equals(active.SignerSubject, previous.SignerSubject, StringComparison.Ordinal)
        && string.Equals(
            NormalizeThumbprint(active.SignerThumbprint),
            NormalizeThumbprint(previous.SignerThumbprint),
            StringComparison.Ordinal)
        && string.Equals(active.BinarySha256, previous.BinarySha256, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeThumbprint(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();

    private static string PrepareEvidenceRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Installed driver evidence root is required.", nameof(value));
        }
        string root = Path.GetFullPath(value);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string RequirePackageId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SudoVdaUpdateStartException(
                "invalid-package-id",
                "Driver package id is required.");
        }
        return value.Trim();
    }
}

internal sealed class SudoVdaUpdateStartException : InvalidOperationException
{
    public SudoVdaUpdateStartException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
