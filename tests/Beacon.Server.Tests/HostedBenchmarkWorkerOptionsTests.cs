using Beacon.Server.TestHost;

namespace Beacon.Server.Tests;

public sealed class HostedBenchmarkWorkerOptionsTests
{
    [Fact]
    public void CreateRejectsMissingExecutablePath()
    {
        using var files = TestFiles.Create();

        Assert.Throws<ArgumentException>(() =>
            HostedBenchmarkWorkerOptions.Create(" ", files.IdentityPath));
    }

    [Fact]
    public void CreateRejectsExecutableThatDoesNotExist()
    {
        using var files = TestFiles.Create();
        string missing = Path.Combine(files.DirectoryPath, "missing-worker");

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() =>
            HostedBenchmarkWorkerOptions.Create(missing, files.IdentityPath));

        Assert.Equal(missing, error.FileName);
    }

    [Fact]
    public void CreateRejectsMissingIdentityPath()
    {
        using var files = TestFiles.Create();

        Assert.Throws<ArgumentException>(() =>
            HostedBenchmarkWorkerOptions.Create(files.ExecutablePath, ""));
    }

    [Fact]
    public void CreateRejectsIdentityThatDoesNotExist()
    {
        using var files = TestFiles.Create();
        string missing = Path.Combine(files.DirectoryPath, "missing-identity.pfx");

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() =>
            HostedBenchmarkWorkerOptions.Create(files.ExecutablePath, missing));

        Assert.Equal(missing, error.FileName);
    }

    [Fact]
    public void CreatePreservesValidatedPaths()
    {
        using var files = TestFiles.Create();

        HostedBenchmarkWorkerOptions options = HostedBenchmarkWorkerOptions.Create(
            files.ExecutablePath,
            files.IdentityPath);

        Assert.Equal(files.ExecutablePath, options.ExecutablePath);
        Assert.Equal(files.IdentityPath, options.IdentityPath);
    }

    private sealed class TestFiles : IDisposable
    {
        private TestFiles(string directoryPath, string executablePath, string identityPath)
        {
            DirectoryPath = directoryPath;
            ExecutablePath = executablePath;
            IdentityPath = identityPath;
        }

        public string DirectoryPath { get; }

        public string ExecutablePath { get; }

        public string IdentityPath { get; }

        public static TestFiles Create()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"beacon-hosted-worker-options-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string executable = Path.Combine(directory, "worker");
            string identity = Path.Combine(directory, "identity.pfx");
            File.WriteAllBytes(executable, []);
            File.WriteAllBytes(identity, []);
            return new TestFiles(directory, executable, identity);
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
