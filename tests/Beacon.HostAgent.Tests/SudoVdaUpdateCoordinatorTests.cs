using Beacon.HostAgent.Contracts;
using Beacon.HostAgent.DriverUpdates;

namespace Beacon.HostAgent.Tests;

public sealed class SudoVdaUpdateCoordinatorTests
{
    [Fact]
    public async Task SuccessfulUpdatePersistsVerifiedCandidateEvidence()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Platform.Evidence.Enqueue(fixture.Previous);
        fixture.Platform.Evidence.Enqueue(fixture.Candidate);

        SudoVdaUpdatePayload accepted = fixture.Coordinator.Start(
            fixture.Package.PackageId,
            fixture.TransactionId,
            reportedActiveLeaseCount: 0);
        SudoVdaUpdatePayload terminal = await fixture.Coordinator.WaitForCompletionAsync(
            fixture.TransactionId,
            CancellationToken.None);

        Assert.Equal(SudoVdaUpdateState.Accepted, accepted.State);
        Assert.Equal(SudoVdaUpdateState.Succeeded, terminal.State);
        Assert.Equal("oem200.inf", terminal.ActiveEvidence?.PublishedInf);
        Assert.Equal(
            new[]
            {
                "host-state",
                "active-evidence",
                "export:oem163.inf",
                "host-state",
                "install:sudovda-22.48.58.193",
                @"restart:ROOT\DISPLAY\0000",
                "active-evidence"
            },
            fixture.Platform.Calls);
        Assert.True(fixture.Journal.TryRead(
            fixture.TransactionId,
            out SudoVdaUpdatePayload? persisted));
        Assert.Equal(terminal, persisted);
    }

    [Fact]
    public void ServiceReportedLeaseBlocksAcceptance()
    {
        using var fixture = new CoordinatorFixture();

        SudoVdaUpdateStartException error = Assert.Throws<SudoVdaUpdateStartException>(() =>
            fixture.Coordinator.Start(
                fixture.Package.PackageId,
                fixture.TransactionId,
                reportedActiveLeaseCount: 1));

        Assert.Equal("service-active-leases", error.Code);
        Assert.False(fixture.Journal.TryRead(fixture.TransactionId, out _));
        Assert.Empty(fixture.Platform.Calls);
    }

    [Theory]
    [InlineData(1, 0, "agent-active-leases")]
    [InlineData(0, 1, "agent-active-virtual-display")]
    public async Task AgentObservedDisplayUseRejectsBeforeMutation(
        int leases,
        int virtualDisplays,
        string expectedDiagnostic)
    {
        using var fixture = new CoordinatorFixture();
        fixture.Platform.HostState = new SudoVdaUpdateHostState(leases, virtualDisplays);

        _ = fixture.Coordinator.Start(
            fixture.Package.PackageId,
            fixture.TransactionId,
            reportedActiveLeaseCount: 0);
        SudoVdaUpdatePayload terminal = await fixture.Coordinator.WaitForCompletionAsync(
            fixture.TransactionId,
            CancellationToken.None);

        Assert.Equal(SudoVdaUpdateState.Rejected, terminal.State);
        Assert.Equal(expectedDiagnostic, terminal.Diagnostic);
        Assert.Equal(new[] { "host-state" }, fixture.Platform.Calls);
    }

    [Fact]
    public async Task ValidationFailureBecomesDurableRejection()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Validator.Error = new SudoVdaPackageValidationException(
            "package-hash-mismatch",
            "hash mismatch");

        _ = fixture.Coordinator.Start(
            fixture.Package.PackageId,
            fixture.TransactionId,
            reportedActiveLeaseCount: 0);
        SudoVdaUpdatePayload terminal = await fixture.Coordinator.WaitForCompletionAsync(
            fixture.TransactionId,
            CancellationToken.None);

        Assert.Equal(SudoVdaUpdateState.Rejected, terminal.State);
        Assert.Equal("package-hash-mismatch", terminal.Diagnostic);
        Assert.DoesNotContain("install", fixture.Platform.Calls);
    }

    [Fact]
    public async Task CandidateVerificationFailureRollsBackExactlyOnce()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Platform.Evidence.Enqueue(fixture.Previous);
        fixture.Platform.Evidence.Enqueue(fixture.Candidate with { BinarySha256 = "BAD" });
        fixture.Platform.Evidence.Enqueue(fixture.Previous);

        _ = fixture.Coordinator.Start(
            fixture.Package.PackageId,
            fixture.TransactionId,
            reportedActiveLeaseCount: 0);
        SudoVdaUpdatePayload terminal = await fixture.Coordinator.WaitForCompletionAsync(
            fixture.TransactionId,
            CancellationToken.None);

        Assert.Equal(SudoVdaUpdateState.RolledBack, terminal.State);
        Assert.Equal(fixture.Previous.ToPayload(), terminal.ActiveEvidence);
        Assert.Equal(1, fixture.Platform.Calls.Count(call => call.StartsWith("rollback:", StringComparison.Ordinal)));
        Assert.Equal(2, fixture.Platform.Calls.Count(call => call.StartsWith("restart:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UnverifiedRollbackBecomesDurableDegradedState()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Platform.Evidence.Enqueue(fixture.Previous);
        fixture.Platform.Evidence.Enqueue(fixture.Candidate with { DriverVersion = "wrong" });
        fixture.Platform.Evidence.Enqueue(fixture.Previous with { DriverVersion = "also-wrong" });

        _ = fixture.Coordinator.Start(
            fixture.Package.PackageId,
            fixture.TransactionId,
            reportedActiveLeaseCount: 0);
        SudoVdaUpdatePayload terminal = await fixture.Coordinator.WaitForCompletionAsync(
            fixture.TransactionId,
            CancellationToken.None);

        Assert.Equal(SudoVdaUpdateState.Degraded, terminal.State);
        Assert.Equal("rollback-verification-failed", terminal.Diagnostic);
        Assert.Equal(1, fixture.Platform.Calls.Count(call => call.StartsWith("rollback:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DuplicateTransactionIsIdempotentButDifferentPackageIsRejected()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Platform.Evidence.Enqueue(fixture.Previous);
        fixture.Platform.Evidence.Enqueue(fixture.Candidate);

        SudoVdaUpdatePayload first = fixture.Coordinator.Start(
            fixture.Package.PackageId,
            fixture.TransactionId,
            reportedActiveLeaseCount: 0);
        SudoVdaUpdatePayload duplicate = fixture.Coordinator.Start(
            fixture.Package.PackageId,
            fixture.TransactionId,
            reportedActiveLeaseCount: 0);
        SudoVdaUpdateStartException conflict = Assert.Throws<SudoVdaUpdateStartException>(() =>
            fixture.Coordinator.Start(
                "different-package",
                fixture.TransactionId,
                reportedActiveLeaseCount: 0));
        _ = await fixture.Coordinator.WaitForCompletionAsync(
            fixture.TransactionId,
            CancellationToken.None);

        Assert.Equal(first, duplicate);
        Assert.Equal("transaction-package-conflict", conflict.Code);
        Assert.Equal(1, fixture.Validator.CallCount);
    }

    [Fact]
    public async Task SecondTransactionIsBusyWhileFirstMutationIsInFlight()
    {
        using var fixture = new CoordinatorFixture(blockInstall: true);
        fixture.Platform.Evidence.Enqueue(fixture.Previous);
        fixture.Platform.Evidence.Enqueue(fixture.Candidate);
        _ = fixture.Coordinator.Start(
            fixture.Package.PackageId,
            fixture.TransactionId,
            reportedActiveLeaseCount: 0);
        await fixture.Platform.InstallStarted.Task;

        SudoVdaUpdateStartException busy = Assert.Throws<SudoVdaUpdateStartException>(() =>
            fixture.Coordinator.Start(
                "sudovda-next",
                Guid.NewGuid(),
                reportedActiveLeaseCount: 0));
        fixture.Platform.AllowInstall.TrySetResult();
        _ = await fixture.Coordinator.WaitForCompletionAsync(
            fixture.TransactionId,
            CancellationToken.None);

        Assert.Equal("driver-update-busy", busy.Code);
    }

    [Fact]
    public async Task CallerCancellationAfterAcceptanceCannotCancelTransaction()
    {
        using var fixture = new CoordinatorFixture(blockInstall: true);
        fixture.Platform.Evidence.Enqueue(fixture.Previous);
        fixture.Platform.Evidence.Enqueue(fixture.Candidate);
        using var caller = new CancellationTokenSource();
        _ = fixture.Coordinator.Start(
            fixture.Package.PackageId,
            fixture.TransactionId,
            reportedActiveLeaseCount: 0);
        Task<SudoVdaUpdatePayload> completion = fixture.Coordinator.WaitForCompletionAsync(
            fixture.TransactionId,
            caller.Token);
        await fixture.Platform.InstallStarted.Task;

        caller.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() => completion);
        fixture.Platform.AllowInstall.TrySetResult();
        SudoVdaUpdatePayload terminal = await fixture.Coordinator.WaitForCompletionAsync(
            fixture.TransactionId,
            CancellationToken.None);

        Assert.Equal(SudoVdaUpdateState.Succeeded, terminal.State);
    }

    private sealed class CoordinatorFixture : IDisposable
    {
        private readonly string root;

        public CoordinatorFixture(bool blockInstall = false)
        {
            root = Path.Combine(Path.GetTempPath(), $"beacon-update-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            string packageRoot = Path.Combine(root, "package");
            Directory.CreateDirectory(packageRoot);
            string infPath = Path.Combine(packageRoot, "SudoVDA.inf");
            File.WriteAllText(infPath, "test");
            Package = new SudoVdaValidatedPackage(
                "sudovda-22.48.58.193",
                "22.48.58.193",
                "0.2.0",
                @"ROOT\SudoMaker\SudoVDA",
                packageRoot,
                infPath,
                "CN=Beacon Test Driver",
                "0123456789ABCDEF",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SudoVDA.dll"] = "CANDIDATE-HASH"
                });
            Previous = Evidence("oem163.inf", "22.48.58.193", "PREVIOUS-HASH");
            Candidate = Evidence("oem200.inf", "22.48.58.193", "CANDIDATE-HASH");
            Validator = new FakePackageValidator(Package);
            Platform = new FakeUpdatePlatform(blockInstall);
            Journal = new SudoVdaUpdateJournal(Path.Combine(root, "journal"));
            Coordinator = new SudoVdaUpdateCoordinator(
                Validator,
                Journal,
                Platform,
                Path.Combine(root, "evidence"));
        }

        public Guid TransactionId { get; } = Guid.NewGuid();

        public SudoVdaValidatedPackage Package { get; }

        public SudoVdaDriverEvidence Previous { get; }

        public SudoVdaDriverEvidence Candidate { get; }

        public FakePackageValidator Validator { get; }

        public FakeUpdatePlatform Platform { get; }

        public SudoVdaUpdateJournal Journal { get; }

        public SudoVdaUpdateCoordinator Coordinator { get; }

        public void Dispose()
        {
            Platform.AllowInstall.TrySetResult();
            Directory.Delete(root, recursive: true);
        }

        private static SudoVdaDriverEvidence Evidence(
            string publishedInf,
            string version,
            string hash) =>
            new(
                @"ROOT\DISPLAY\0000",
                @"root\sudomaker\sudovda",
                publishedInf,
                version,
                "0.2.0",
                "CN=Beacon Test Driver",
                "0123456789ABCDEF",
                hash,
                DeviceHealthy: true);
    }

    private sealed class FakePackageValidator(SudoVdaValidatedPackage package)
        : ISudoVdaPackageValidator
    {
        public Exception? Error { get; set; }

        public int CallCount { get; private set; }

        public Task<SudoVdaValidatedPackage> ValidateAndStageAsync(
            string packageId,
            Guid transactionId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Error is null
                ? Task.FromResult(package with { PackageId = packageId })
                : Task.FromException<SudoVdaValidatedPackage>(Error);
        }
    }

    private sealed class FakeUpdatePlatform(bool blockInstall) : ISudoVdaUpdatePlatform
    {
        public List<string> Calls { get; } = [];

        public Queue<SudoVdaDriverEvidence> Evidence { get; } = [];

        public SudoVdaUpdateHostState HostState { get; set; } = new(0, 0);

        public TaskCompletionSource InstallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowInstall { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SudoVdaUpdateHostState> QueryHostStateAsync()
        {
            Calls.Add("host-state");
            return Task.FromResult(HostState);
        }

        public Task<SudoVdaDriverEvidence> QueryActiveEvidenceAsync()
        {
            Calls.Add("active-evidence");
            return Task.FromResult(Evidence.Dequeue());
        }

        public Task ExportActivePackageAsync(
            SudoVdaDriverEvidence evidence,
            string destinationRoot)
        {
            Calls.Add($"export:{evidence.PublishedInf}");
            Directory.CreateDirectory(destinationRoot);
            File.WriteAllText(Path.Combine(destinationRoot, "SudoVDA.inf"), "exported");
            return Task.CompletedTask;
        }

        public async Task InstallAsync(SudoVdaValidatedPackage package)
        {
            Calls.Add($"install:{package.PackageId}");
            InstallStarted.TrySetResult();
            if (blockInstall)
            {
                await AllowInstall.Task;
            }
        }

        public Task RestartAsync(string deviceInstanceId)
        {
            Calls.Add($"restart:{deviceInstanceId}");
            return Task.CompletedTask;
        }

        public Task RollbackAsync(
            string exportedPackageRoot,
            SudoVdaDriverEvidence previousEvidence)
        {
            Calls.Add($"rollback:{previousEvidence.PublishedInf}");
            return Task.CompletedTask;
        }
    }
}
