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
        Assert.False(options.VideoEnabled);
    }

    [Fact]
    public void CreateRequiresBothHostedVideoVectors()
    {
        using var files = TestFiles.Create();

        Assert.Throws<ArgumentException>(() => HostedBenchmarkWorkerOptions.Create(
            files.ExecutablePath,
            files.IdentityPath,
            files.Video720pPath,
            null));
    }

    [Fact]
    public void CreatePreservesValidatedHostedVideoVectors()
    {
        using var files = TestFiles.Create();

        HostedBenchmarkWorkerOptions options = HostedBenchmarkWorkerOptions.Create(
            files.ExecutablePath,
            files.IdentityPath,
            files.Video720pPath,
            files.Video360pPath);

        Assert.True(options.VideoEnabled);
        Assert.Equal(files.Video720pPath, options.Video720pPath);
        Assert.Equal(files.Video360pPath, options.Video360pPath);
    }

    private sealed class TestFiles : IDisposable
    {
        private TestFiles(
            string directoryPath,
            string executablePath,
            string identityPath,
            string video720pPath,
            string video360pPath)
        {
            DirectoryPath = directoryPath;
            ExecutablePath = executablePath;
            IdentityPath = identityPath;
            Video720pPath = video720pPath;
            Video360pPath = video360pPath;
        }

        public string DirectoryPath { get; }

        public string ExecutablePath { get; }

        public string IdentityPath { get; }

        public string Video720pPath { get; }

        public string Video360pPath { get; }

        public static TestFiles Create()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"beacon-hosted-worker-options-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string executable = Path.Combine(directory, "worker");
            string identity = Path.Combine(directory, "identity.pfx");
            string video720p = Path.Combine(directory, "video-720p.bau");
            string video360p = Path.Combine(directory, "video-360p.bau");
            File.WriteAllBytes(executable, []);
            File.WriteAllBytes(identity, []);
            File.WriteAllBytes(video720p, []);
            File.WriteAllBytes(video360p, []);
            return new TestFiles(directory, executable, identity, video720p, video360p);
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
