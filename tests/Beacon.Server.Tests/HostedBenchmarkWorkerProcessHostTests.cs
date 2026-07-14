using System.Diagnostics;
using System.ComponentModel;
using Beacon.Platform.Windows.Streaming;
using Beacon.Server.TestHost;
using Beacon.StreamWorker.Contracts.Framing;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Server.Tests;

public sealed class HostedBenchmarkWorkerProcessHostTests
{
    [Fact]
    public async Task DuplexStreamReadsFromOutputAndWritesToInput()
    {
        await using var read = new MemoryStream([1, 2, 3]);
        await using var write = new MemoryStream();
        await using var duplex = new ReadWriteDuplexStream(read, write, leaveOpen: true);
        byte[] received = new byte[3];

        await duplex.ReadExactlyAsync(received);
        await duplex.WriteAsync(new byte[] { 4, 5, 6 });
        await duplex.FlushAsync();

        Assert.Equal(new byte[] { 1, 2, 3 }, received);
        Assert.Equal(new byte[] { 4, 5, 6 }, write.ToArray());
        Assert.False(duplex.CanSeek);
    }

    [Fact]
    public async Task StartsLazilyAndCompletesHandshakeAndCorrelatedCommand()
    {
        using var fixture = HostedWorkerFixture.Create("normal");
        await using var host = fixture.CreateHost();

        Assert.False(host.IsReady);
        Assert.Equal(0, host.ProcessId);

        await host.EnsureReadyAsync(CancellationToken.None);
        long generation = host.CurrentProcessGeneration;
        StreamWorkerCommandResponse response = await host.SendAsync(
            generation,
            Command("correlated-session"),
            CancellationToken.None);

        Assert.True(host.IsReady);
        Assert.True(generation > 0);
        Assert.True(response.Completion.RequestId > 0);
        Assert.Equal("correlated-session", response.Completion.SessionId);
        Assert.True(response.Completion.WorkerCompletion.Succeeded);
        Assert.Equal(
            host.WorkerInstanceId.ToArray(),
            host.Capabilities.WorkerInstanceId.ToByteArray());
    }

    [Fact]
    public async Task InvalidExecutablePreservesLaunchFailureAndLeavesNoActiveProcess()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"beacon-hosted-worker-invalid-executable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string executable = Path.Combine(directory, "not-an-executable.exe");
        string identity = Path.Combine(directory, "identity.pfx");
        File.WriteAllBytes(executable, []);
        File.WriteAllBytes(identity, []);

        try
        {
            await using var host = new HostedBenchmarkWorkerProcessHost(
                HostedBenchmarkWorkerOptions.Create(executable, identity));

            await Assert.ThrowsAsync<Win32Exception>(() =>
                host.EnsureReadyAsync(CancellationToken.None));
            Assert.Equal(0, host.ProcessId);
            Assert.False(host.IsReady);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PublishesTranslatedWorkerEvents()
    {
        using var fixture = HostedWorkerFixture.Create("normal");
        await using var host = fixture.CreateHost();
        var events = host.Events;

        await host.EnsureReadyAsync(CancellationToken.None);
        StreamWorkerEvent workerEvent = await events.ReadAsync(CancellationToken.None);

        StreamWorkerConnectionObserved observed = Assert.IsType<StreamWorkerConnectionObserved>(workerEvent);
        Assert.Equal(host.CurrentProcessGeneration, observed.ProcessGeneration);
        Assert.Equal(41ul, observed.ConnectionGeneration);
    }

    [Fact]
    public async Task GracefulShutdownUsesTypedCommandAndAllowsNewGeneration()
    {
        using var fixture = HostedWorkerFixture.Create("normal");
        await using var host = fixture.CreateHost();

        await host.EnsureReadyAsync(CancellationToken.None);
        long firstGeneration = host.CurrentProcessGeneration;
        int firstProcessId = host.ProcessId;

        await host.ShutdownAsync(CancellationToken.None);

        Assert.False(host.IsReady);
        Assert.Equal(0, host.ProcessId);
        Assert.Contains("BEACON_FAKE_HOSTED_WORKER_STOPPED", host.Diagnostics);
        AssertProcessExited(firstProcessId);

        await host.EnsureReadyAsync(CancellationToken.None);

        Assert.True(host.IsReady);
        Assert.True(host.CurrentProcessGeneration > firstGeneration);
    }

    [Fact]
    public async Task ChildExitFailsCommandAndPublishesGenerationExit()
    {
        using var fixture = HostedWorkerFixture.Create("exit-on-command");
        await using var host = fixture.CreateHost();
        var events = host.Events;

        await host.EnsureReadyAsync(CancellationToken.None);
        long generation = host.CurrentProcessGeneration;

        await Assert.ThrowsAnyAsync<IOException>(
            () => host.SendAsync(generation, Command("exit-session"), CancellationToken.None));
        StreamWorkerProcessExited exited = Assert.IsType<StreamWorkerProcessExited>(
            await events.ReadAsync(CancellationToken.None));

        Assert.Equal(generation, exited.ProcessGeneration);
        Assert.Equal(23, exited.ExitCode);
        Assert.False(host.IsReady);
    }

    [Fact]
    public async Task DisposalTerminatesChildAfterBrokenControlChannel()
    {
        using var fixture = HostedWorkerFixture.Create("break-on-command");
        var host = fixture.CreateHost();

        await host.EnsureReadyAsync(CancellationToken.None);
        int processId = host.ProcessId;
        long generation = host.CurrentProcessGeneration;
        await Assert.ThrowsAsync<ProtobufFrameException>(
            () => host.SendAsync(generation, Command("broken-session"), CancellationToken.None));

        await host.DisposeAsync();

        AssertProcessExited(processId);
    }

    [Fact]
    public async Task StderrDiagnosticsAreBoundedAndDoNotExposeIdentityPath()
    {
        using var fixture = HostedWorkerFixture.Create("diagnostics");
        await using var host = fixture.CreateHost(diagnosticCapacity: 4);

        await host.EnsureReadyAsync(CancellationToken.None);
        await host.ShutdownAsync(CancellationToken.None);

        Assert.Equal(4, host.Diagnostics.Count);
        Assert.All(host.Diagnostics, line => Assert.DoesNotContain(fixture.IdentityPath, line, StringComparison.Ordinal));
        Assert.Equal("BEACON_FAKE_HOSTED_WORKER_STOPPED", host.Diagnostics[^1]);
    }

    private static WorkerIpcEnvelope Command(string sessionId) => new()
    {
        SessionId = sessionId,
        PrepareBenchmark = new PrepareBenchmark(),
    };

    private static void AssertProcessExited(int processId) =>
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(processId));

    private sealed class HostedWorkerFixture : IDisposable
    {
        private HostedWorkerFixture(string directoryPath, string executablePath, string identityPath)
        {
            DirectoryPath = directoryPath;
            ExecutablePath = executablePath;
            IdentityPath = identityPath;
        }

        public string DirectoryPath { get; }

        public string ExecutablePath { get; }

        public string IdentityPath { get; }

        public static HostedWorkerFixture Create(string mode)
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"beacon-hosted-worker-process-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string testHostAssembly = typeof(TestHostProgram).Assembly.Location;
            string executable = Path.ChangeExtension(testHostAssembly, ".exe");
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException("Beacon TestHost apphost is unavailable.", executable);
            }
            string identity = Path.Combine(directory, "identity-mode.txt");
            File.WriteAllText(identity, mode);
            return new HostedWorkerFixture(directory, executable, identity);
        }

        public HostedBenchmarkWorkerProcessHost CreateHost(int diagnosticCapacity = 16)
        {
            HostedBenchmarkWorkerOptions options = HostedBenchmarkWorkerOptions.Create(
                ExecutablePath,
                IdentityPath);
            return new HostedBenchmarkWorkerProcessHost(
                options,
                CreateStartInfo,
                eventCapacity: 16,
                diagnosticCapacity);
        }

        private static ProcessStartInfo CreateStartInfo(HostedBenchmarkWorkerOptions options)
        {
            ProcessStartInfo startInfo = HostedBenchmarkWorkerProcessHost.CreateStartInfo(options);
            startInfo.ArgumentList.Insert(0, "--fake-hosted-worker");
            return startInfo;
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
