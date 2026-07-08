using Beacon.Platform.Windows.Streaming;
using Beacon.StreamingProbe;

namespace Beacon.StreamingProbe.Tests;

public sealed class StreamingProbeAppTests
{
    [Fact]
    public async Task RunWritesRuntimeDescriptorAndExitsWhenOnceIsSet()
    {
        using TempDirectory temp = TempDirectory.Create();
        string descriptorPath = Path.Combine(temp.Path, "session.json");
        var command = new StreamingProbeCommand(
            "z-fold-7-steam-shortcut:3767414131",
            "client-z-fold-7",
            descriptorPath,
            "gamestream",
            "moonlight://beacon/runtime/session",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["input"] = "udp://127.0.0.1:48000",
                ["rtsp"] = "rtsp://127.0.0.1:48010/beacon"
            },
            Once: true);
        var lifetime = new RecordingStreamingProbeLifetime();
        using var output = new StringWriter();

        int exitCode = await StreamingProbeApp.RunAsync(command, output, TextWriter.Null, lifetime);

        Assert.Equal(0, exitCode);
        Assert.False(lifetime.Waited);
        Assert.Contains("descriptorPath=", output.ToString(), StringComparison.Ordinal);
        var store = new WindowsExternalStreamingSessionDescriptorStore(temp.Path);
        ExternalStreamingSessionDescriptorReadResult read = store.Read(descriptorPath);
        Assert.True(read.Success, read.Error);
        ExternalStreamingSessionDescriptor descriptor = Assert.IsType<ExternalStreamingSessionDescriptor>(read.Descriptor);
        Assert.Equal("gamestream", descriptor.Protocol);
        Assert.Equal("moonlight://beacon/runtime/session", descriptor.LaunchUri);
        Assert.NotNull(descriptor.Endpoints);
        Assert.Equal("rtsp://127.0.0.1:48010/beacon", descriptor.Endpoints["rtsp"]);
        Assert.NotNull(descriptor.Metadata);
        Assert.Equal("z-fold-7-steam-shortcut:3767414131", descriptor.Metadata["sessionId"]);
        Assert.Equal("client-z-fold-7", descriptor.Metadata["displayId"]);
        Assert.NotNull(descriptor.Diagnostics);
        Assert.Contains("streaming probe descriptor ready", descriptor.Diagnostics);
    }

    [Fact]
    public async Task RunStartsConfiguredChildProcessAndStopsItWhenLifetimeEnds()
    {
        using TempDirectory temp = TempDirectory.Create();
        string descriptorPath = Path.Combine(temp.Path, "session.json");
        var command = new StreamingProbeCommand(
            "z-fold-7-steam-shortcut:3767414131",
            "client-z-fold-7",
            descriptorPath,
            "gamestream",
            "moonlight://beacon/runtime/session",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["rtsp"] = "rtsp://127.0.0.1:48010/beacon"
            },
            Once: false,
            ChildExecutable: "C:\\Tools\\sunshine.exe",
            ChildArguments: "--config sunshine.json",
            ChildEnvironment: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BEACON_SESSION_ID"] = "z-fold-7-steam-shortcut:3767414131",
                ["BEACON_DISPLAY_ID"] = "client-z-fold-7",
                ["BEACON_STREAM_SESSION_DESCRIPTOR_PATH"] = descriptorPath
            });
        var lifetime = new RecordingStreamingProbeLifetime();
        var childRunner = new RecordingStreamingProbeChildProcessRunner(["C:\\Tools\\sunshine.exe"]);
        using var output = new StringWriter();

        int exitCode = await StreamingProbeApp.RunAsync(command, output, TextWriter.Null, lifetime, childRunner);

        Assert.Equal(0, exitCode);
        Assert.True(lifetime.Waited);
        StreamingProbeChildCommand childCommand = Assert.Single(childRunner.StartedCommands);
        Assert.Equal("C:\\Tools\\sunshine.exe", childCommand.FileName);
        Assert.Equal("--config sunshine.json", childCommand.Arguments);
        Assert.Equal("client-z-fold-7", childCommand.Environment["BEACON_DISPLAY_ID"]);
        Assert.Equal([2001], childRunner.StoppedProcessIds);
        Assert.Contains("childProcessId=2001", output.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(descriptorPath));
    }

    [Fact]
    public async Task RunDoesNotWriteRuntimeDescriptorWhenConfiguredChildProcessIsMissing()
    {
        using TempDirectory temp = TempDirectory.Create();
        string descriptorPath = Path.Combine(temp.Path, "session.json");
        var command = new StreamingProbeCommand(
            "session",
            "display",
            descriptorPath,
            "gamestream",
            "moonlight://beacon/runtime/session",
            new Dictionary<string, string>(),
            Once: false,
            ChildExecutable: "C:\\Tools\\missing-sunshine.exe",
            ChildArguments: "--config sunshine.json");
        var lifetime = new RecordingStreamingProbeLifetime();
        var childRunner = new RecordingStreamingProbeChildProcessRunner();
        using var error = new StringWriter();

        int exitCode = await StreamingProbeApp.RunAsync(command, TextWriter.Null, error, lifetime, childRunner);

        Assert.Equal(2, exitCode);
        Assert.False(lifetime.Waited);
        Assert.Empty(childRunner.StartedCommands);
        Assert.False(File.Exists(descriptorPath));
        Assert.Contains("child executable 'C:\\Tools\\missing-sunshine.exe' does not exist", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunReturnsChildExitCodeWhenChildProcessExitsBeforeStop()
    {
        using TempDirectory temp = TempDirectory.Create();
        string descriptorPath = Path.Combine(temp.Path, "session.json");
        var command = new StreamingProbeCommand(
            "session",
            "display",
            descriptorPath,
            "gamestream",
            "moonlight://beacon/runtime/session",
            new Dictionary<string, string>(),
            Once: false,
            ChildExecutable: "C:\\Tools\\sunshine.exe");
        var lifetime = new PendingStreamingProbeLifetime();
        var childRunner = new RecordingStreamingProbeChildProcessRunner(["C:\\Tools\\sunshine.exe"])
        {
            ExitCode = 17
        };
        using var output = new StringWriter();

        int exitCode = await StreamingProbeApp.RunAsync(command, output, TextWriter.Null, lifetime, childRunner);

        Assert.Equal(17, exitCode);
        Assert.True(lifetime.Waited);
        Assert.Empty(childRunner.StoppedProcessIds);
        Assert.Contains("childExitCode=17", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWaitsForStopWhenOnceIsNotSet()
    {
        using TempDirectory temp = TempDirectory.Create();
        var command = new StreamingProbeCommand(
            "session",
            "display",
            Path.Combine(temp.Path, "session.json"),
            "gamestream",
            "moonlight://beacon/runtime/session",
            new Dictionary<string, string>(),
            Once: false);
        var lifetime = new RecordingStreamingProbeLifetime();

        int exitCode = await StreamingProbeApp.RunAsync(command, TextWriter.Null, TextWriter.Null, lifetime);

        Assert.Equal(0, exitCode);
        Assert.True(lifetime.Waited);
    }

    [Fact]
    public async Task RunReportsParseErrorsWithoutWaiting()
    {
        var lifetime = new RecordingStreamingProbeLifetime();
        using var error = new StringWriter();

        int exitCode = await StreamingProbeApp.RunAsync(
            [],
            new Dictionary<string, string>(),
            TextWriter.Null,
            error,
            lifetime);

        Assert.Equal(2, exitCode);
        Assert.False(lifetime.Waited);
        Assert.Contains("Missing required option --session", error.ToString(), StringComparison.Ordinal);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"beacon-streaming-probe-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class RecordingStreamingProbeLifetime : IStreamingProbeLifetime
    {
        public bool Waited { get; private set; }

        public Task WaitForStopAsync()
        {
            Waited = true;
            return Task.CompletedTask;
        }
    }

    private sealed class PendingStreamingProbeLifetime : IStreamingProbeLifetime
    {
        public bool Waited { get; private set; }

        public Task WaitForStopAsync()
        {
            Waited = true;
            return TaskCompletionSourceProvider.Never<object?>();
        }
    }

    private sealed class RecordingStreamingProbeChildProcessRunner : IStreamingProbeChildProcessRunner
    {
        private int nextProcessId = 2001;

        public RecordingStreamingProbeChildProcessRunner(IEnumerable<string>? existingFiles = null)
        {
            if (existingFiles is null)
            {
                return;
            }

            foreach (string path in existingFiles)
            {
                ExistingFiles.Add(path);
            }
        }

        public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<StreamingProbeChildCommand> StartedCommands { get; } = [];

        public List<int> StoppedProcessIds { get; } = [];

        public int? ExitCode { get; set; }

        public bool FileExists(string path) => ExistingFiles.Contains(path);

        public StreamingProbeChildProcess Start(StreamingProbeChildCommand command)
        {
            StartedCommands.Add(command);
            return new StreamingProbeChildProcess(nextProcessId++);
        }

        public Task<int?> WaitForExitAsync(StreamingProbeChildProcess process) =>
            ExitCode.HasValue
                ? Task.FromResult<int?>(ExitCode.Value)
                : TaskCompletionSourceProvider.Never<int?>();

        public void Stop(StreamingProbeChildProcess process)
        {
            StoppedProcessIds.Add(process.ProcessId);
        }
    }

    private static class TaskCompletionSourceProvider
    {
        public static Task<T> Never<T>()
        {
            var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            return source.Task;
        }
    }
}
