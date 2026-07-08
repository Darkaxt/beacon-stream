using Beacon.Core.Clients;
using Beacon.Core.Diagnostics;
using Beacon.Core.Displays;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;
using Beacon.Platform.Windows.Streaming;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class ExternalProcessStreamingBackendTests
{
    [Fact]
    public async Task GetHealthAsyncReportsExecutableAndManifestCapabilitiesWithoutStartingProcess()
    {
        var reader = new FakeExternalStreamingManifestReader();
        reader.Manifests["C:\\Tools\\beacon-streaming.json"] = new ExternalStreamingManifest(
            "Sunshine bridge",
            "gamestream",
            "moonlight://beacon/z-fold-7",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["rtsp"] = "rtsp://127.0.0.1:48010/beacon"
            },
            ["av1", "hevc"],
            120,
            150,
            Hdr10: true,
            ["lan-direct"],
            ["nvenc"],
            ["dxgi"],
            ["ready"]);
        var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe", ManifestPath: "C:\\Tools\\beacon-streaming.json"),
            runner,
            reader);

        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.True(health.Ready);
        Assert.Equal("external-process", health.Backend);
        Assert.True(health.ExecutableConfigured);
        Assert.True(health.ExecutableAvailable);
        Assert.True(health.ManifestConfigured);
        Assert.True(health.ManifestAvailable);
        Assert.Equal("Sunshine bridge", health.ManifestName);
        Assert.True(health.Hdr10);
        Assert.Equal(["av1", "hevc"], health.Codecs);
        Assert.Equal(["lan-direct"], health.Transports);
        Assert.Equal(0, health.ActiveSessions);
        Assert.Empty(runner.StartedCommands);
        Assert.Empty(runner.StoppedProcessIds);
    }

    [Fact]
    public async Task GetHealthAsyncReportsMissingExecutableAsNotReady()
    {
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\missing-wrapper.exe"),
            new FakeExternalStreamingProcessRunner());

        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.False(health.Ready);
        Assert.True(health.ExecutableConfigured);
        Assert.False(health.ExecutableAvailable);
        Assert.Contains("does not exist", health.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

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
    public async Task PreflightFailurePublishesDiagnostic()
    {
        var sink = new RecordingDiagnosticSink();
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\missing-wrapper.exe"),
            new FakeExternalStreamingProcessRunner(),
            manifestReader: null,
            sink);

        StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        DiagnosticEvent evt = Assert.Single(sink.Events);
        Assert.Equal("streaming", evt.Category);
        Assert.Equal("preflight", evt.Operation);
        Assert.Equal("error", evt.Severity);
        Assert.Equal("z-fold-7-steam-shortcut:3767414131", evt.SessionId);
        Assert.Contains("missing-wrapper.exe", evt.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task GetSessionsMarksRunningSessionExitedWhenWrapperProcessExited()
    {
        var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe"),
            runner);
        SessionPlan plan = CreatePlan();
        await backend.StartAsync(plan, CancellationToken.None);
        runner.MarkExited(processId: 1001, exitCode: 3221225781);

        StreamingSessionState session = Assert.Single(backend.GetSessions());
        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.Equal("exited", session.State);
        Assert.Contains("3221225781", session.Error, StringComparison.Ordinal);
        Assert.Equal(0, health.ActiveSessions);
        Assert.Contains(health.Diagnostics, diagnostic => diagnostic.Contains("exited", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExitedWrapperDiagnosticsIncludeBoundedProcessOutput()
    {
        var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe"),
            runner);
        SessionPlan plan = CreatePlan();
        await backend.StartAsync(plan, CancellationToken.None);
        runner.MarkExited(
            processId: 1001,
            exitCode: 1,
            diagnostics:
            [
                "stdout: wrapper booted",
                "stderr: encoder failed: NVENC unavailable"
            ]);

        StreamingSessionState session = Assert.Single(backend.GetSessions());
        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.Equal("exited", session.State);
        Assert.Contains("encoder failed", session.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(health.Diagnostics, diagnostic => diagnostic.Contains("wrapper booted", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(health.Diagnostics, diagnostic => diagnostic.Contains("NVENC unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StartIncludesConfiguredConnectionDescriptor()
    {
        var reader = new FakeExternalStreamingManifestReader();
        reader.Manifests["C:\\Tools\\beacon-streaming.json"] = new ExternalStreamingManifest(
            "ignored",
            "manifest-protocol",
            "manifest://ignored",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ignored"] = "manifest://endpoint"
            },
            ["av1"],
            120,
            150,
            Hdr10: false,
            ["lan-direct"],
            [],
            [],
            []);
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
            },
            ManifestPath: "C:\\Tools\\beacon-streaming.json");
        var backend = new ExternalProcessStreamingBackend(options, runner, reader);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.NotNull(session.Connection);
        Assert.Equal("gamestream", session.Connection.Protocol);
        Assert.Equal("moonlight://beacon/z-fold-7-steam-shortcut:3767414131", session.Connection.LaunchUri);
        Assert.Contains(session.Connection.Endpoints, endpoint => endpoint.Role == "rtsp" && endpoint.Uri == "rtsp://127.0.0.1:48010/beacon");
        Assert.DoesNotContain(session.Connection.Endpoints, endpoint => endpoint.Role == "ignored");
        ExternalStreamingCommand command = Assert.Single(runner.StartedCommands);
        Assert.Equal("gamestream", command.Environment["BEACON_CONNECTION_PROTOCOL"]);
        Assert.Equal("moonlight://beacon/z-fold-7-steam-shortcut:3767414131", command.Environment["BEACON_CONNECTION_LAUNCH_URI"]);
        Assert.Equal("input=udp://127.0.0.1:48000;rtsp=rtsp://127.0.0.1:48010/beacon", command.Environment["BEACON_CONNECTION_ENDPOINTS"]);
        Assert.Equal("C:\\Tools\\beacon-streaming.json", command.Environment["BEACON_WRAPPER_MANIFEST_PATH"]);
    }

    [Fact]
    public async Task StartUsesManifestConnectionWhenExplicitConnectionIsAbsent()
    {
        var reader = new FakeExternalStreamingManifestReader();
        reader.Manifests["C:\\Tools\\beacon-streaming.json"] = new ExternalStreamingManifest(
            "Sunshine bridge",
            "gamestream",
            "moonlight://beacon/z-fold-7-steam-shortcut:3767414131",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["rtsp"] = "rtsp://127.0.0.1:48010/beacon"
            },
            ["av1", "hevc", "h264"],
            120,
            150,
            Hdr10: false,
            ["lan-direct"],
            ["nvenc"],
            ["dxgi"],
            ["ready"]);
        var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe", ManifestPath: "C:\\Tools\\beacon-streaming.json"),
            runner,
            reader);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.NotNull(session.Connection);
        Assert.Equal("gamestream", session.Connection.Protocol);
        Assert.Equal("moonlight://beacon/z-fold-7-steam-shortcut:3767414131", session.Connection.LaunchUri);
        Assert.Contains(session.Connection.Endpoints, endpoint => endpoint.Role == "rtsp" && endpoint.Uri == "rtsp://127.0.0.1:48010/beacon");
        Assert.Equal("C:\\Tools\\beacon-streaming.json", session.Connection.Metadata["manifestPath"]);
        ExternalStreamingCommand command = Assert.Single(runner.StartedCommands);
        Assert.Equal("C:\\Tools\\beacon-streaming.json", command.Environment["BEACON_WRAPPER_MANIFEST_PATH"]);
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

        public Dictionary<int, ExternalStreamingProcessStatus> ProcessStatuses { get; } = [];

        public string? NextStopError { get; set; }

        public bool FileExists(string path) => ExistingFiles.Contains(path);

        public ExternalStreamingProcess Start(ExternalStreamingCommand command)
        {
            StartedCommands.Add(command);
            int processId = nextProcessId++;
            ProcessStatuses[processId] = ExternalStreamingProcessStatus.Running();
            return new ExternalStreamingProcess(processId);
        }

        public ExternalStreamingProcessStopResult Stop(ExternalStreamingProcess process)
        {
            if (!string.IsNullOrWhiteSpace(NextStopError))
            {
                return ExternalStreamingProcessStopResult.Fail(NextStopError);
            }

            StoppedProcessIds.Add(process.ProcessId);
            ProcessStatuses[process.ProcessId] = ExternalStreamingProcessStatus.Exited(0);
            return ExternalStreamingProcessStopResult.Ok();
        }

        public ExternalStreamingProcessStatus GetStatus(ExternalStreamingProcess process) =>
            ProcessStatuses.GetValueOrDefault(process.ProcessId, ExternalStreamingProcessStatus.Exited(null));

        public void MarkExited(int processId, long exitCode) =>
            ProcessStatuses[processId] = ExternalStreamingProcessStatus.Exited(exitCode);

        public void MarkExited(int processId, long exitCode, IReadOnlyList<string> diagnostics) =>
            ProcessStatuses[processId] = ExternalStreamingProcessStatus.Exited(exitCode, diagnostics);
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

    private sealed class RecordingDiagnosticSink : IDiagnosticEventSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Publish(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }
}
