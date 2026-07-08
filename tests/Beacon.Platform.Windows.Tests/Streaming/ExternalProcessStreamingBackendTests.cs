using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Streaming;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class ExternalProcessStreamingBackendTests
{
    [Fact]
    public void CreateStartInfoPassesSessionPlanAsArgumentsAndEnvironment()
    {
        SessionPlan plan = CreatePlan();

        ExternalStreamingCommand command = ExternalProcessStreamingBackend.CreateStartCommand(
            "C:\\Tools\\sunshine-wrapper.exe",
            plan);

        Assert.Equal("C:\\Tools\\sunshine-wrapper.exe", command.FileName);
        Assert.Contains("--session", command.Arguments);
        Assert.Contains("z-fold-7-steam-shortcut:3767414131", command.Arguments);
        Assert.Equal("client-z-fold-7", command.Environment["BEACON_DISPLAY_ID"]);
        Assert.Equal("av1", command.Environment["BEACON_STREAM_CODEC"]);
        Assert.Equal("120", command.Environment["BEACON_STREAM_FPS"]);
        Assert.Equal("65", command.Environment["BEACON_STREAM_BITRATE_MBPS"]);
    }

    [Fact]
    public async Task PreflightFailsWhenExecutablePathIsMissing()
    {
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions(null),
            new FakeExternalStreamingProcessRunner());

        StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("External streaming executable path is not configured", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreflightFailsWhenExecutableDoesNotExist()
    {
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\missing-wrapper.exe"),
            new FakeExternalStreamingProcessRunner());

        StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreflightFailsWhenConfiguredManifestIsMissing()
    {
        var reader = new FakeExternalStreamingManifestReader();
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe", ManifestPath: "C:\\Tools\\missing-manifest.json"),
            new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]),
            reader);

        StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("External streaming manifest 'C:\\Tools\\missing-manifest.json' does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreflightRejectsPlanUnsupportedByManifest()
    {
        var reader = new FakeExternalStreamingManifestReader();
        reader.Manifests["C:\\Tools\\beacon-streaming.json"] = new ExternalStreamingManifest(
            "Sunshine bridge",
            "gamestream",
            null,
            new Dictionary<string, string>(),
            ["h264"],
            60,
            40,
            Hdr10: false,
            ["lan-direct"],
            ["software"],
            ["dxgi"],
            ["AV1 disabled"]);
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe", ManifestPath: "C:\\Tools\\beacon-streaming.json"),
            new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]),
            reader);

        StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("codec av1 is not supported", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartUsesRunnerAndRecordsRunningSession()
    {
        var runner = new FakeExternalStreamingProcessRunner();
        runner.ExistingFiles.Add("C:\\Tools\\sunshine-wrapper.exe");
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe"),
            runner);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.Equal("running", session.State);
        Assert.Equal("client-z-fold-7", session.DisplayId);
        ExternalStreamingCommand command = Assert.Single(runner.StartedCommands);
        Assert.Equal("C:\\Tools\\sunshine-wrapper.exe", command.FileName);
        Assert.Equal("120", command.Environment["BEACON_STREAM_FPS"]);
        Assert.Single(backend.GetSessions());
    }

    [Fact]
    public async Task StartIncludesConfiguredConnectionDescriptor()
    {
        var runner = new FakeExternalStreamingProcessRunner();
        runner.ExistingFiles.Add("C:\\Tools\\sunshine-wrapper.exe");
        var options = new ExternalProcessStreamingOptions(
            "C:\\Tools\\sunshine-wrapper.exe",
            "gamestream",
            "moonlight://beacon/z-fold-7-steam-shortcut:3767414131",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["rtsp"] = "rtsp://127.0.0.1:48010/beacon",
                ["input"] = "udp://127.0.0.1:48000"
            });
        var backend = new ExternalProcessStreamingBackend(options, runner);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.NotNull(session.Connection);
        Assert.Equal("gamestream", session.Connection.Protocol);
        Assert.Equal("moonlight://beacon/z-fold-7-steam-shortcut:3767414131", session.Connection.LaunchUri);
        Assert.Contains(session.Connection.Endpoints, endpoint => endpoint.Role == "rtsp" && endpoint.Uri == "rtsp://127.0.0.1:48010/beacon");
        ExternalStreamingCommand command = Assert.Single(runner.StartedCommands);
        Assert.Equal("gamestream", command.Environment["BEACON_CONNECTION_PROTOCOL"]);
        Assert.Equal("moonlight://beacon/z-fold-7-steam-shortcut:3767414131", command.Environment["BEACON_CONNECTION_LAUNCH_URI"]);
        Assert.Equal("input=udp://127.0.0.1:48000;rtsp=rtsp://127.0.0.1:48010/beacon", command.Environment["BEACON_CONNECTION_ENDPOINTS"]);
    }

    [Fact]
    public async Task StopTerminatesOwnedProcessAndMarksSessionStopped()
    {
        var runner = new FakeExternalStreamingProcessRunner();
        runner.ExistingFiles.Add("C:\\Tools\\sunshine-wrapper.exe");
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe"),
            runner);
        SessionPlan plan = CreatePlan();
        await backend.StartAsync(plan, CancellationToken.None);

        StreamingStopResult result = await backend.StopAsync(plan.SessionId, CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.Equal("stopped", session.State);
        Assert.Equal([1001], runner.StoppedProcessIds);
    }

    [Fact]
    public async Task StopFailureIsRecordedAndReturned()
    {
        var runner = new FakeExternalStreamingProcessRunner { NextStopError = "wrapper refused stop" };
        runner.ExistingFiles.Add("C:\\Tools\\sunshine-wrapper.exe");
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe"),
            runner);
        SessionPlan plan = CreatePlan();
        await backend.StartAsync(plan, CancellationToken.None);

        StreamingStopResult result = await backend.StopAsync(plan.SessionId, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("wrapper refused stop", result.Error, StringComparison.Ordinal);
        StreamingSessionState? session = await backend.GetSessionAsync(plan.SessionId, CancellationToken.None);
        Assert.Equal("stop-failed", session?.State);
        Assert.Equal("wrapper refused stop", session?.Error);
    }

    private static SessionPlan CreatePlan() =>
        new(
            "z-fold-7-steam-shortcut:3767414131",
            new ClientId("z-fold-7"),
            "steam-shortcut:3767414131",
            new PlannedDisplay("client-z-fold-7", 2560, 1600, 120, "virtual-primary", HdrPreference.Prefer, false, "sdr", "HDR unavailable."),
            new PlannedStream("av1", 120, 65, "lan-direct", "adaptive"));

    private sealed class FakeExternalStreamingProcessRunner : IExternalStreamingProcessRunner
    {
        private int nextProcessId = 1001;

        public FakeExternalStreamingProcessRunner(IEnumerable<string>? existingFiles = null)
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

        public List<ExternalStreamingCommand> StartedCommands { get; } = [];

        public List<int> StoppedProcessIds { get; } = [];

        public string? NextStopError { get; set; }

        public bool FileExists(string path) => ExistingFiles.Contains(path);

        public ExternalStreamingProcess Start(ExternalStreamingCommand command)
        {
            StartedCommands.Add(command);
            return new ExternalStreamingProcess(nextProcessId++);
        }

        public ExternalStreamingProcessStopResult Stop(ExternalStreamingProcess process)
        {
            if (!string.IsNullOrWhiteSpace(NextStopError))
            {
                return ExternalStreamingProcessStopResult.Fail(NextStopError);
            }

            StoppedProcessIds.Add(process.ProcessId);
            return ExternalStreamingProcessStopResult.Ok();
        }
    }

    private sealed class FakeExternalStreamingManifestReader : IExternalStreamingManifestReader
    {
        public Dictionary<string, ExternalStreamingManifest> Manifests { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => Manifests.ContainsKey(path);

        public ExternalStreamingManifestReadResult Read(string path) =>
            Manifests.TryGetValue(path, out ExternalStreamingManifest? manifest)
                ? ExternalStreamingManifestReadResult.Ok(manifest)
                : ExternalStreamingManifestReadResult.Fail($"External streaming manifest '{path}' does not exist.");
    }
}
