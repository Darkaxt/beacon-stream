using System.Security.Cryptography;
using Beacon.HostAgent.Contracts;
using Beacon.HostAgent.HostUpdates;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Tests;

public sealed class HostAgentUpdateCoordinatorTests
{
    [Fact]
    public async Task ValidPackageIsProtectedRevalidatedAndStagedDurably()
    {
        using var fixture = new UpdateFixture();
        await fixture.BuildPackageAsync();
        Guid transactionId = Guid.NewGuid();

        HostAgentUpdatePayload result = await fixture.Coordinator.StageAsync(
            fixture.PackageId,
            transactionId,
            CancellationToken.None);

        Assert.Equal(HostAgentUpdateState.Staged, result.State);
        Assert.Equal(fixture.SourceCommit, result.SourceCommit);
        Assert.Equal("agent-current", result.PreviousVersionId);
        Assert.True(Directory.Exists(Path.Combine(
            fixture.Storage.StagedPackages,
            transactionId.ToString("D"))));
        HostAgentPendingUpdate pending = fixture.State.ReadPending()
            ?? throw new Xunit.Sdk.XunitException("Pending update was not persisted.");
        Assert.Equal(transactionId, pending.TransactionId);
        Assert.Equal(fixture.PackageId, pending.PackageId);
        Assert.True(fixture.Journal.TryRead(transactionId, out HostAgentUpdatePayload? journaled));
        Assert.Equal(HostAgentUpdateState.Staged, journaled?.State);
    }

    [Fact]
    public async Task SameTransactionAndPackageIsIdempotent()
    {
        using var fixture = new UpdateFixture();
        await fixture.BuildPackageAsync();
        Guid transactionId = Guid.NewGuid();
        HostAgentUpdatePayload first = await fixture.Coordinator.StageAsync(
            fixture.PackageId,
            transactionId,
            CancellationToken.None);

        HostAgentUpdatePayload replay = await fixture.Coordinator.StageAsync(
            fixture.PackageId,
            transactionId,
            CancellationToken.None);

        Assert.Equal(first, replay);
    }

    [Fact]
    public async Task TransactionCannotBeReusedForAnotherPackage()
    {
        using var fixture = new UpdateFixture();
        await fixture.BuildPackageAsync();
        Guid transactionId = Guid.NewGuid();
        await fixture.Coordinator.StageAsync(
            fixture.PackageId,
            transactionId,
            CancellationToken.None);

        HostAgentUpdateStartException error = await Assert.ThrowsAsync<
            HostAgentUpdateStartException>(() => fixture.Coordinator.StageAsync(
                "agent-other",
                transactionId,
                CancellationToken.None));

        Assert.Equal("transaction-package-conflict", error.Code);
    }

    [Fact]
    public async Task PendingTransactionBlocksAnotherUpdate()
    {
        using var fixture = new UpdateFixture();
        await fixture.BuildPackageAsync();
        await fixture.Coordinator.StageAsync(
            fixture.PackageId,
            Guid.NewGuid(),
            CancellationToken.None);

        HostAgentUpdateStartException error = await Assert.ThrowsAsync<
            HostAgentUpdateStartException>(() => fixture.Coordinator.StageAsync(
                fixture.PackageId,
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("host-agent-update-busy", error.Code);
    }

    [Fact]
    public async Task TamperedInboxPackageNeverCreatesPendingState()
    {
        using var fixture = new UpdateFixture();
        await fixture.BuildPackageAsync();
        File.AppendAllText(
            Path.Combine(fixture.Storage.Inbox, fixture.PackageId, "payload", "Beacon.HostAgent.exe"),
            "tampered");

        HostAgentUpdateStartException error = await Assert.ThrowsAsync<
            HostAgentUpdateStartException>(() => fixture.Coordinator.StageAsync(
                fixture.PackageId,
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("package-length-mismatch", error.Code);
        Assert.Null(fixture.State.ReadPending());
    }

    private sealed class UpdateFixture : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            $"beacon-host-agent-coordinator-{Guid.NewGuid():N}");
        private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public UpdateFixture()
        {
            Storage = new HostAgentUpdateStorage(Path.Combine(root, "storage"));
            State = new HostAgentUpdateStateStore(Storage.State);
            State.WriteCurrent(new HostAgentSelectedVersion("agent-current", new string('B', 40)));
            Journal = new HostAgentUpdateJournal(Storage.Transactions);
            var validator = new HostAgentUpdatePackageValidator(
                key.ExportSubjectPublicKeyInfoPem(),
                new Version(1, 0, 0));
            Coordinator = new HostAgentUpdateCoordinator(Storage, validator, Journal, State);
        }

        public string PackageId { get; } = "agent-0123456789abcdef";

        public string SourceCommit { get; } = new('A', 40);

        public HostAgentUpdateStorage Storage { get; }

        public HostAgentUpdateStateStore State { get; }

        public HostAgentUpdateJournal Journal { get; }

        public HostAgentUpdateCoordinator Coordinator { get; }

        public async Task BuildPackageAsync()
        {
            string source = Path.Combine(root, "payload");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Beacon.HostAgent.exe"), "agent-binary");
            await HostAgentUpdatePackageBuilder.BuildAsync(
                source,
                Path.Combine(Storage.Inbox, PackageId),
                new HostAgentUpdateBuildIdentity(PackageId, SourceCommit, "1.0.0"),
                key.ExportPkcs8PrivateKeyPem(),
                CancellationToken.None);
        }

        public void Dispose()
        {
            key.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
