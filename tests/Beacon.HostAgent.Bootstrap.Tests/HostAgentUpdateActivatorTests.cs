using System.Security.Cryptography;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Bootstrap.Tests;

public sealed class HostAgentUpdateActivatorTests
{
    [Fact]
    public async Task SignedProtectedPackageInstallsExactPayloadIntoFreshVersion()
    {
        using var fixture = new ActivatorFixture();
        HostAgentPendingUpdate pending = await fixture.BuildPackageAsync();

        HostAgentSelectedVersion result = await fixture.Activator.ActivateAsync(
            pending,
            fixture.Current);

        Assert.Equal(fixture.PackageId, result.VersionId);
        string installedRoot = fixture.Storage.GetVersionRoot(fixture.PackageId);
        Assert.Equal("agent-binary", File.ReadAllText(Path.Combine(
            installedRoot,
            "Beacon.HostAgent.exe")));
        Assert.Equal(
            new[] { "Beacon.HostAgent.exe", "runtime.dll" },
            Directory.EnumerateFiles(installedRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(installedRoot, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public async Task TamperedProtectedPackageIsRejectedBeforeVersionCreation()
    {
        using var fixture = new ActivatorFixture();
        HostAgentPendingUpdate pending = await fixture.BuildPackageAsync();
        File.AppendAllText(
            Path.Combine(pending.StagedPackageRoot, "payload", "Beacon.HostAgent.exe"),
            "tampered");

        await Assert.ThrowsAsync<HostAgentUpdateValidationException>(() =>
            fixture.Activator.ActivateAsync(pending, fixture.Current));

        Assert.False(Directory.Exists(fixture.Storage.GetVersionRoot(fixture.PackageId)));
    }

    [Fact]
    public async Task ExistingVersionMustMatchSignedManifestExactly()
    {
        using var fixture = new ActivatorFixture();
        HostAgentPendingUpdate pending = await fixture.BuildPackageAsync();
        await fixture.Activator.ActivateAsync(pending, fixture.Current);
        File.AppendAllText(
            Path.Combine(
                fixture.Storage.GetVersionRoot(fixture.PackageId),
                "Beacon.HostAgent.exe"),
            "tampered");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Activator.ActivateAsync(pending, fixture.Current));
    }

    [Fact]
    public async Task StagedPathOutsideTransactionDirectoryIsRejected()
    {
        using var fixture = new ActivatorFixture();
        HostAgentPendingUpdate pending = await fixture.BuildPackageAsync();
        HostAgentPendingUpdate escaped = pending with
        {
            StagedPackageRoot = Path.Combine(fixture.Storage.StagedPackagesRoot, "other")
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Activator.ActivateAsync(escaped, fixture.Current));
    }

    private sealed class ActivatorFixture : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            $"beacon-bootstrap-activator-{Guid.NewGuid():N}");
        private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public ActivatorFixture()
        {
            Storage = new BootstrapStorage(
                Path.Combine(root, "install"),
                Path.Combine(root, "data"));
            var validator = new HostAgentUpdatePackageValidator(
                key.ExportSubjectPublicKeyInfoPem(),
                new Version(1, 0, 0));
            Activator = new HostAgentUpdateActivator(Storage, validator);
        }

        public string PackageId { get; } = "agent-0123456789abcdef";

        public HostAgentSelectedVersion Current { get; } =
            new("agent-current", new string('B', 40));

        public BootstrapStorage Storage { get; }

        public HostAgentUpdateActivator Activator { get; }

        public async Task<HostAgentPendingUpdate> BuildPackageAsync()
        {
            Guid transactionId = Guid.NewGuid();
            string source = Path.Combine(root, "payload");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Beacon.HostAgent.exe"), "agent-binary");
            File.WriteAllText(Path.Combine(source, "runtime.dll"), "runtime-binary");
            string staged = Path.Combine(Storage.StagedPackagesRoot, transactionId.ToString("D"));
            await HostAgentUpdatePackageBuilder.BuildAsync(
                source,
                staged,
                new HostAgentUpdateBuildIdentity(PackageId, new string('A', 40), "1.0.0"),
                key.ExportPkcs8PrivateKeyPem(),
                CancellationToken.None);
            return new HostAgentPendingUpdate(
                transactionId,
                PackageId,
                new string('A', 40),
                Current.VersionId,
                Current.SourceCommit,
                staged);
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
