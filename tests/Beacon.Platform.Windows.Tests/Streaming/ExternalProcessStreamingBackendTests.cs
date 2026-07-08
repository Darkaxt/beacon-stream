using System.Diagnostics;
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
    public async Task GetHealthAsyncReportsMissingWrapperChildExecutableAsNotReady()
    {
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions(
                "C:\\Tools\\beacon-streaming-probe.exe",
                WrapperChildExecutablePath: "C:\\Tools\\missing-sunshine.exe"),
            new FakeExternalStreamingProcessRunner(["C:\\Tools\\beacon-streaming-probe.exe"]));

        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.False(health.Ready);
        Assert.True(health.WrapperChildExecutableConfigured);
        Assert.False(health.WrapperChildExecutableAvailable);
        Assert.Equal("C:\\Tools\\missing-sunshine.exe", health.WrapperChildExecutablePath);
        Assert.Contains("wrapper child executable", health.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing-sunshine.exe", health.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHealthAsyncReportsWrapperChildExecutableReadiness()
    {
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions(
                "C:\\Tools\\beacon-streaming-probe.exe",
                WrapperChildExecutablePath: "C:\\Tools\\sunshine.exe",
                WrapperChildArguments: "--config sunshine.json"),
            new FakeExternalStreamingProcessRunner(
            [
                "C:\\Tools\\beacon-streaming-probe.exe",
                "C:\\Tools\\sunshine.exe"
            ]));

        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.True(health.Ready);
        Assert.True(health.WrapperChildExecutableConfigured);
        Assert.True(health.WrapperChildExecutableAvailable);
        Assert.Equal("C:\\Tools\\sunshine.exe", health.WrapperChildExecutablePath);
        Assert.True(health.WrapperChildArgumentsConfigured);
    }

    [Fact]
    public async Task GetHealthAsyncReportsWrapperChildArgumentsWithoutExecutableAsNotReady()
    {
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions(
                "C:\\Tools\\beacon-streaming-probe.exe",
                WrapperChildArguments: "--config sunshine.json"),
            new FakeExternalStreamingProcessRunner(["C:\\Tools\\beacon-streaming-probe.exe"]));

        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.False(health.Ready);
        Assert.False(health.WrapperChildExecutableConfigured);
        Assert.True(health.WrapperChildArgumentsConfigured);
        Assert.Contains("wrapper child arguments", health.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("child executable", health.Diagnostic, StringComparison.OrdinalIgnoreCase);
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
    public void CreateStartCommandPassesWrapperChildConfiguration()
    {
        SessionPlan plan = CreatePlan();

        ExternalStreamingCommand command = ExternalProcessStreamingBackend.CreateStartCommand(
            "C:\\Tools\\beacon-streaming-probe.exe",
            plan,
            new ExternalProcessStreamingOptions(
                "C:\\Tools\\beacon-streaming-probe.exe",
                WrapperChildExecutablePath: "C:\\Tools\\sunshine.exe",
                WrapperChildArguments: "--config sunshine.json"));

        Assert.Equal("C:\\Tools\\sunshine.exe", command.Environment["BEACON_WRAPPER_CHILD_EXECUTABLE"]);
        Assert.Equal("--config sunshine.json", command.Environment["BEACON_WRAPPER_CHILD_ARGUMENTS"]);
    }

    [Fact]
    public void CreateStartCommandDoesNotPassWrapperChildArgumentsWithoutExecutable()
    {
        SessionPlan plan = CreatePlan();

        ExternalStreamingCommand command = ExternalProcessStreamingBackend.CreateStartCommand(
            "C:\\Tools\\beacon-streaming-probe.exe",
            plan,
            new ExternalProcessStreamingOptions(
                "C:\\Tools\\beacon-streaming-probe.exe",
                WrapperChildArguments: "--config sunshine.json"));

        Assert.DoesNotContain("BEACON_WRAPPER_CHILD_ARGUMENTS", command.Environment.Keys);
    }

    [Fact]
    public void WindowsRunnerCreatesStartInfoWithWrapperWorkingDirectory()
    {
        var command = new ExternalStreamingCommand(
            "C:\\Tools\\BeaconWrapper\\beacon-wrapper.exe",
            "--session session-1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BEACON_SESSION_ID"] = "session-1"
            });

        ProcessStartInfo startInfo = WindowsExternalStreamingProcessRunner.CreateStartInfo(command);

        Assert.Equal("C:\\Tools\\BeaconWrapper\\beacon-wrapper.exe", startInfo.FileName);
        Assert.Equal("C:\\Tools\\BeaconWrapper", startInfo.WorkingDirectory);
        Assert.Equal("--session session-1", startInfo.Arguments);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal("session-1", startInfo.Environment["BEACON_SESSION_ID"]);
    }

    [Fact]
    public void SunshineEndpointProfileUsesDocumentedPortOffsets()
    {
        var profile = new SunshineEndpointProfile("127.0.0.1", 47989);

        IReadOnlyDictionary<string, string> endpoints = profile.CreateEndpoints();

        Assert.Equal("https://127.0.0.1:47984", endpoints["https"]);
        Assert.Equal("http://127.0.0.1:47989", endpoints["http"]);
        Assert.Equal("https://127.0.0.1:47990", endpoints["web"]);
        Assert.Equal("rtsp://127.0.0.1:48010", endpoints["rtsp"]);
        Assert.Equal("udp://127.0.0.1:47998", endpoints["video"]);
        Assert.Equal("udp://127.0.0.1:47999", endpoints["control"]);
        Assert.Equal("udp://127.0.0.1:48000", endpoints["audio"]);
        Assert.Equal("udp://127.0.0.1:48002", endpoints["mic"]);
    }

    [Fact]
    public async Task GetHealthAsyncReportsSunshineEndpointProfile()
    {
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions(
                "C:\\Tools\\sunshine-wrapper.exe",
                SunshineProfile: new SunshineEndpointProfile("127.0.0.1", 47989)),
            new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]));

        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.True(health.Ready);
        Assert.Equal("gamestream", health.Protocol);
        Assert.Contains(health.Endpoints, endpoint => endpoint.Role == "rtsp" && endpoint.Uri == "rtsp://127.0.0.1:48010");
        Assert.Contains(health.Endpoints, endpoint => endpoint.Role == "audio" && endpoint.Uri == "udp://127.0.0.1:48000");
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
    public async Task PreflightFailsWhenConfiguredWrapperChildExecutableDoesNotExist()
    {
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions(
                "C:\\Tools\\beacon-streaming-probe.exe",
                WrapperChildExecutablePath: "C:\\Tools\\missing-sunshine.exe"),
            new FakeExternalStreamingProcessRunner(["C:\\Tools\\beacon-streaming-probe.exe"]));

        StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("wrapper child executable", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing-sunshine.exe", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreflightFailsWhenWrapperChildArgumentsAreConfiguredWithoutExecutable()
    {
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions(
                "C:\\Tools\\beacon-streaming-probe.exe",
                WrapperChildArguments: "--config sunshine.json"),
            new FakeExternalStreamingProcessRunner(["C:\\Tools\\beacon-streaming-probe.exe"]));

        StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("wrapper child arguments", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("child executable", result.Error, StringComparison.OrdinalIgnoreCase);
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
    public async Task StartIncludesSunshineEndpointProfileWhenExplicitEndpointsAreAbsent()
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
        var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
        var options = new ExternalProcessStreamingOptions(
            "C:\\Tools\\sunshine-wrapper.exe",
            ManifestPath: "C:\\Tools\\beacon-streaming.json",
            SunshineProfile: new SunshineEndpointProfile("127.0.0.1", 47989));
        var backend = new ExternalProcessStreamingBackend(options, runner, reader);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.NotNull(session.Connection);
        Assert.Equal("gamestream", session.Connection.Protocol);
        Assert.Contains(session.Connection.Endpoints, endpoint => endpoint.Role == "rtsp" && endpoint.Uri == "rtsp://127.0.0.1:48010");
        Assert.Contains(session.Connection.Endpoints, endpoint => endpoint.Role == "video" && endpoint.Uri == "udp://127.0.0.1:47998");
        Assert.DoesNotContain(session.Connection.Endpoints, endpoint => endpoint.Role == "ignored");
        ExternalStreamingCommand command = Assert.Single(runner.StartedCommands);
        Assert.Equal("gamestream", command.Environment["BEACON_CONNECTION_PROTOCOL"]);
        Assert.Equal(
            "audio=udp://127.0.0.1:48000;control=udp://127.0.0.1:47999;http=http://127.0.0.1:47989;https=https://127.0.0.1:47984;mic=udp://127.0.0.1:48002;rtsp=rtsp://127.0.0.1:48010;video=udp://127.0.0.1:47998;web=https://127.0.0.1:47990",
            command.Environment["BEACON_CONNECTION_ENDPOINTS"]);
    }

    [Fact]
    public async Task ExplicitEndpointsOverrideSunshineEndpointProfile()
    {
        var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
        var options = new ExternalProcessStreamingOptions(
            "C:\\Tools\\sunshine-wrapper.exe",
            ConnectionEndpoints: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["rtsp"] = "rtsp://10.0.0.10:49010/session"
            },
            SunshineProfile: new SunshineEndpointProfile("127.0.0.1", 47989));
        var backend = new ExternalProcessStreamingBackend(options, runner);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.NotNull(session.Connection);
        Assert.Equal("gamestream", session.Connection.Protocol);
        Assert.Contains(session.Connection.Endpoints, endpoint => endpoint.Role == "rtsp" && endpoint.Uri == "rtsp://10.0.0.10:49010/session");
        Assert.Contains(session.Connection.Endpoints, endpoint => endpoint.Role == "audio" && endpoint.Uri == "udp://127.0.0.1:48000");
        ExternalStreamingCommand command = Assert.Single(runner.StartedCommands);
        Assert.Contains("rtsp=rtsp://10.0.0.10:49010/session", command.Environment["BEACON_CONNECTION_ENDPOINTS"], StringComparison.Ordinal);
        Assert.DoesNotContain("rtsp=rtsp://127.0.0.1:48010", command.Environment["BEACON_CONNECTION_ENDPOINTS"], StringComparison.Ordinal);
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
    public async Task StartPassesRuntimeDescriptorPathAndUsesDescriptorConnectionWhenWrapperWritesIt()
    {
        var descriptors = new FakeExternalStreamingSessionDescriptorStore();
        var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
        runner.OnStart = command =>
        {
            string descriptorPath = command.Environment["BEACON_STREAM_SESSION_DESCRIPTOR_PATH"];
            Assert.DoesNotContain(descriptorPath, descriptors.DescriptorsByPath.Keys);
            descriptors.DescriptorsByPath[descriptorPath] = new ExternalStreamingSessionDescriptor(
                "gamestream",
                "moonlight://beacon/runtime/z-fold-7-steam-shortcut:3767414131",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rtsp"] = "rtsp://127.0.0.1:48010/runtime",
                    ["input"] = "udp://127.0.0.1:48000"
                },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["pairing"] = "already-paired"
                },
                ["runtime descriptor ready"]);
        };
        string stalePath = descriptors.PreviewDescriptorPath(CreatePlan().SessionId);
        descriptors.DescriptorsByPath[stalePath] = new ExternalStreamingSessionDescriptor(
            "stale",
            "moonlight://stale",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            []);
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe"),
            runner,
            sessionDescriptors: descriptors);

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.NotNull(session.Connection);
        Assert.Equal("gamestream", session.Connection.Protocol);
        Assert.Equal("moonlight://beacon/runtime/z-fold-7-steam-shortcut:3767414131", session.Connection.LaunchUri);
        Assert.Contains(session.Connection.Endpoints, endpoint => endpoint.Role == "rtsp" && endpoint.Uri == "rtsp://127.0.0.1:48010/runtime");
        Assert.Equal("already-paired", session.Connection.Metadata["pairing"]);
        Assert.Equal(stalePath, session.Connection.Metadata["sessionDescriptorPath"]);
        Assert.Equal([CreatePlan().SessionId], descriptors.PreparedSessionIds);
        Assert.Contains(stalePath, descriptors.ClearedDescriptorPaths);
        ExternalStreamingCommand started = Assert.Single(runner.StartedCommands);
        Assert.Equal(stalePath, started.Environment["BEACON_STREAM_SESSION_DESCRIPTOR_PATH"]);
        Assert.Contains("--stream-session-descriptor", started.Arguments, StringComparison.Ordinal);
        Assert.Contains(stalePath, started.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSessionRefreshesConnectionFromRuntimeDescriptorWrittenAfterStart()
    {
        var descriptors = new FakeExternalStreamingSessionDescriptorStore();
        var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe"),
            runner,
            sessionDescriptors: descriptors);
        SessionPlan plan = CreatePlan();
        StreamingStartResult start = await backend.StartAsync(plan, CancellationToken.None);
        StreamingSessionState started = Assert.IsType<StreamingSessionState>(start.Session);
        Assert.Null(started.Connection);
        string descriptorPath = Assert.Single(runner.StartedCommands).Environment["BEACON_STREAM_SESSION_DESCRIPTOR_PATH"];
        descriptors.DescriptorsByPath[descriptorPath] = new ExternalStreamingSessionDescriptor(
            "gamestream",
            "moonlight://beacon/runtime/late",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["rtsp"] = "rtsp://127.0.0.1:48010/late"
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = "wrapper-runtime"
            },
            ["descriptor arrived after start"]);

        StreamingSessionState? refreshed = await backend.GetSessionAsync(plan.SessionId, CancellationToken.None);

        Assert.NotNull(refreshed?.Connection);
        Assert.Equal("gamestream", refreshed.Connection.Protocol);
        Assert.Equal("moonlight://beacon/runtime/late", refreshed.Connection.LaunchUri);
        Assert.Contains(refreshed.Connection.Endpoints, endpoint => endpoint.Role == "rtsp" && endpoint.Uri == "rtsp://127.0.0.1:48010/late");
        Assert.Equal("wrapper-runtime", refreshed.Connection.Metadata["source"]);
    }

    [Fact]
    public async Task RuntimeDescriptorReadFailureIsRecordedOnSessionAndHealth()
    {
        var descriptors = new FakeExternalStreamingSessionDescriptorStore();
        var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
        var backend = new ExternalProcessStreamingBackend(
            new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe"),
            runner,
            sessionDescriptors: descriptors);
        SessionPlan plan = CreatePlan();
        await backend.StartAsync(plan, CancellationToken.None);
        string descriptorPath = Assert.Single(runner.StartedCommands).Environment["BEACON_STREAM_SESSION_DESCRIPTOR_PATH"];
        descriptors.FailuresByPath[descriptorPath] = "runtime descriptor JSON is malformed";

        StreamingSessionState? session = await backend.GetSessionAsync(plan.SessionId, CancellationToken.None);
        StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);

        Assert.Equal("runtime descriptor JSON is malformed", session?.Error);
        Assert.Contains(health.Diagnostics, diagnostic => diagnostic.Contains("runtime descriptor JSON is malformed", StringComparison.Ordinal));
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

        public Action<ExternalStreamingCommand>? OnStart { get; set; }

        public bool FileExists(string path) => ExistingFiles.Contains(path);

        public ExternalStreamingProcess Start(ExternalStreamingCommand command)
        {
            StartedCommands.Add(command);
            OnStart?.Invoke(command);
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

    private sealed class FakeExternalStreamingSessionDescriptorStore : IExternalStreamingSessionDescriptorStore
    {
        public Dictionary<string, ExternalStreamingSessionDescriptor> DescriptorsByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> FailuresByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> PreparedSessionIds { get; } = [];

        public List<string> ClearedDescriptorPaths { get; } = [];

        public string PreviewDescriptorPath(string sessionId) => $"C:\\Beacon\\Runtime\\{sessionId}.json";

        public string? PrepareDescriptorPath(string sessionId)
        {
            PreparedSessionIds.Add(sessionId);
            string path = PreviewDescriptorPath(sessionId);
            DescriptorsByPath.Remove(path);
            ClearedDescriptorPaths.Add(path);
            return path;
        }

        public ExternalStreamingSessionDescriptorReadResult Read(string path) =>
            FailuresByPath.TryGetValue(path, out string? failure)
                ? ExternalStreamingSessionDescriptorReadResult.Fail(failure)
                : DescriptorsByPath.TryGetValue(path, out ExternalStreamingSessionDescriptor? descriptor)
                ? ExternalStreamingSessionDescriptorReadResult.Ok(descriptor)
                : ExternalStreamingSessionDescriptorReadResult.NotFound();

        public void Delete(string path)
        {
            DescriptorsByPath.Remove(path);
        }
    }

    private sealed class RecordingDiagnosticSink : IDiagnosticEventSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Publish(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }
}
