using Beacon.HostAgent.DriverUpdates;

namespace Beacon.HostAgent.Tests;

public sealed class HostAgentDriverUpdateStorageTests
{
    [Fact]
    public void StorageUsesFixedCanonicalChildrenBelowOneRoot()
    {
        using var root = new TemporaryDirectory();

        var storage = new HostAgentDriverUpdateStorage(root.Path);

        Assert.Equal(Path.Combine(root.Path, "Inbox"), storage.Inbox);
        Assert.Equal(Path.Combine(root.Path, "Staged"), storage.Staged);
        Assert.Equal(Path.Combine(root.Path, "InstalledEvidence"), storage.InstalledEvidence);
        Assert.Equal(Path.Combine(root.Path, "Transactions"), storage.Transactions);
        Assert.Equal(Path.Combine(root.Path, "Logs"), storage.Logs);
        Assert.All(
            new[]
            {
                storage.Inbox,
                storage.Staged,
                storage.InstalledEvidence,
                storage.Transactions,
                storage.Logs
            },
            path => Assert.True(Directory.Exists(path)));
    }

    [Fact]
    public void SignerPolicyIsBootstrappedOnceAndThenRemainsPinned()
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "driver-policy.json");
        var store = new SudoVdaPackagePolicyStore(path);
        var first = new SudoVdaSignatureEvidence(
            true,
            "CN=Beacon Driver A",
            "AAAA",
            "valid");
        var replacement = new SudoVdaSignatureEvidence(
            true,
            "CN=Beacon Driver B",
            "BBBB",
            "valid");

        SudoVdaPackagePolicy created = store.LoadOrCreate(first);
        SudoVdaPackagePolicy loaded = store.LoadOrCreate(replacement);

        Assert.Equal("CN=Beacon Driver A", created.SignerSubject);
        Assert.Equal("AAAA", created.SignerThumbprint);
        Assert.Equal(created, loaded);
        Assert.Equal("x64", loaded.Architecture);
        Assert.Equal(@"ROOT\SudoMaker\SudoVDA", loaded.HardwareId);
        Assert.Equal(new Version(0, 2, 0), loaded.MinimumProtocolVersion);
    }

    [Fact]
    public void InvalidBootstrapSignatureCannotCreatePolicy()
    {
        using var root = new TemporaryDirectory();
        var store = new SudoVdaPackagePolicyStore(Path.Combine(root.Path, "driver-policy.json"));

        Assert.Throws<InvalidDataException>(() => store.LoadOrCreate(
            new SudoVdaSignatureEvidence(
                false,
                string.Empty,
                string.Empty,
                "invalid")));
        Assert.False(File.Exists(Path.Combine(root.Path, "driver-policy.json")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"beacon-driver-storage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
