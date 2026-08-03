using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beacon.Core.Tests.Architecture;

public sealed class ArchitectureRecoveryBoundaryTests
{
    private static readonly string[] ProtectedDirectories =
    [
        "src/Beacon.Core/Clients",
        "src/Beacon.Core/Displays",
        "src/Beacon.Core/Games",
        "src/Beacon.Core/Recovery",
        "src/Beacon.Core/Sessions",
        "src/Beacon.Platform.Windows/Displays",
        "src/Beacon.Platform.Windows/Games",
        "src/Beacon.Platform.Windows/Recovery",
        "src/Beacon.Android/app/src/main/java/dev/beacon/android"
    ];

    private static readonly Lazy<GuardDefinition> GuardValue = new(LoadGuardDefinition);

    private static GuardDefinition Guard => GuardValue.Value;

    private static Regex CompatibilityPattern => Guard.CompatibilityPattern;

    private static readonly NativeBoundaryRule[] NativeBoundaryRules =
    [
        new(
            "MsQuic",
            new Regex("MsQuic|HQUIC|QUIC_[A-Z0-9_]", RegexOptions.CultureInvariant),
            [
                "src/Beacon.StreamProtocol",
                "src/Beacon.StreamWorker",
                "src/Beacon.Android/app/src/main/cpp",
                "native",
                "scripts",
                "tests/Beacon.StreamProtocol.Tests",
                "tests/Beacon.StreamWorker.Tests"
            ]),
        new(
            "NVENC",
            new Regex("NvEncodeAPI|NV_ENC_[A-Z0-9_]", RegexOptions.CultureInvariant),
            [
                "src/Beacon.StreamWorker",
                "tests/Beacon.StreamWorker.Tests",
                "native/vendor/nv-codec-headers"
            ]),
        new(
            "Windows Graphics Capture",
            new Regex(
                "Windows\\.Graphics\\.Capture|GraphicsCapture(Item|Session)|Direct3D11CaptureFramePool",
                RegexOptions.CultureInvariant),
            [
                "src/Beacon.StreamWorker",
                "tests/Beacon.StreamWorker.Tests"
            ]),
        new(
            "Android MediaCodec",
            new Regex("android\\.media\\.MediaCodec|MediaCodec", RegexOptions.CultureInvariant),
            [
                "src/Beacon.Android/app/src/main/java",
                "src/Beacon.Android/app/src/main/cpp",
                "src/Beacon.Android/app/src/test",
                "src/Beacon.Android/app/src/androidTest"
            ])
    ];

    [Fact]
    public void RecoveryAuthorityAndProtectedBoundariesExist()
    {
        string root = FindRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(
            root,
            "docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md")));
        Assert.True(File.Exists(Path.Combine(
            root,
            "docs/source-audits/2026-07-10-beacon-architecture-recovery-inventory.md")));

        foreach (string relative in ProtectedDirectories)
        {
            Assert.True(Directory.Exists(ToPlatformPath(root, relative)), relative);
        }
    }

    [Fact]
    public void AndroidDecoderHasOneConcreteSurfaceOwner()
    {
        string root = FindRepositoryRoot();

        Assert.False(File.Exists(ToPlatformPath(
            root,
            "src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoDecoder.java")));
        Assert.DoesNotContain(
            "implements EncodedVideoDecoder",
            File.ReadAllText(ToPlatformPath(
                root,
                "src/Beacon.Android/app/src/main/java/dev/beacon/android/SurfaceEncodedVideoDecoder.java")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void StreamWorkerBackendHasOneAuthoritativeHostContract()
    {
        string root = FindRepositoryRoot();
        string host = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Platform.Windows/Streaming/StreamWorkerProcessHost.cs"));
        string backend = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Platform.Windows/Streaming/StreamWorkerStreamingBackend.cs"));
        string registration = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Server/Hosting/BeaconServiceRegistration.cs"));

        Assert.DoesNotContain("IGenerationBoundStreamWorkerHost", host, StringComparison.Ordinal);
        Assert.DoesNotContain("IGenerationBoundStreamWorkerHost", backend, StringComparison.Ordinal);
        Assert.DoesNotContain("IGenerationBoundStreamWorkerHost", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyGenerationBoundStreamWorkerHost", backend, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreStreamAuthorizationContractIsRuntimeNeutral()
    {
        string root = FindRepositoryRoot();
        string authorization = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Core/Streaming/IStreamSessionAuthorizer.cs"));

        Assert.DoesNotContain("Worker", authorization, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerTicketOwnershipIsRuntimeNeutral()
    {
        string root = FindRepositoryRoot();
        string ticketService = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Server/Security/StreamTicketService.cs"));
        string provisioning = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Server/Security/StreamTicketProvisioningService.cs"));

        Assert.DoesNotContain("Beacon.StreamWorker.Contracts", ticketService, StringComparison.Ordinal);
        Assert.DoesNotContain("Beacon.StreamWorker.Contracts", provisioning, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateWorkerAuthorization", ticketService, StringComparison.Ordinal);
    }

    [Fact]
    public void HostedRunnerVideoExceptionIsExplicitAndNarrow()
    {
        string root = FindRepositoryRoot();
        string script = File.ReadAllText(ToPlatformPath(
            root,
            "scripts/test-stream-worker-integration.ps1"));
        string workflow = File.ReadAllText(ToPlatformPath(
            root,
            ".github/workflows/ci.yml"));
        string processHostTests = File.ReadAllText(ToPlatformPath(
            root,
            "tests/Beacon.Platform.Windows.Tests/Streaming/StreamWorkerProcessHostTests.cs"));
        string nativeProbe = File.ReadAllText(ToPlatformPath(
            root,
            "tests/Beacon.StreamWorker.Tests/quic_listener_probe.cpp"));

        Assert.Contains("[switch]$AllowUnsupportedVideoHardware", script, StringComparison.Ordinal);
        Assert.Contains(
            "$env:BEACON_TEST_ALLOW_UNSUPPORTED_VIDEO_HARDWARE = '1'",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Remove-Item Env:BEACON_TEST_ALLOW_UNSUPPORTED_VIDEO_HARDWARE",
            script,
            StringComparison.Ordinal);
        Assert.Contains("$nativeExitCode -eq 99", script, StringComparison.Ordinal);
        Assert.Contains(
            "BEACON_WORKER_VIDEO_FAILURE CAPTURE 5",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "BEACON_WORKER_VIDEO_FAILURE PREPARE CAPABILITY_UNAVAILABLE",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "BEACON_WORKER_VIDEO_FAILURE PREPARE CAPABILITY_UNAVAILABLE",
            nativeProbe,
            StringComparison.Ordinal);
        Assert.Contains(
            "WORKER_ERROR_CODE_CAPABILITY_UNAVAILABLE",
            nativeProbe,
            StringComparison.Ordinal);
        Assert.Contains(".video_available()", nativeProbe, StringComparison.Ordinal);
        Assert.Contains(".audio_available()", nativeProbe, StringComparison.Ordinal);
        Assert.Contains(
            "BEACON_WORKER_PREPARE_FAILURE",
            nativeProbe,
            StringComparison.Ordinal);
        Assert.Contains(
            "BEACON_WORKER_PREPARE_EXCHANGE_FAILURE",
            nativeProbe,
            StringComparison.Ordinal);
        Assert.DoesNotContain("$nativeExitCode -eq 88", script, StringComparison.Ordinal);
        Assert.Contains(
            "./scripts/test-stream-worker-integration.ps1 -AllowUnsupportedVideoHardware",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("Prepare(\"integration-session\")", processHostTests, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionWorkerCapabilitiesComeFromTheIdentityBoundHandshake()
    {
        string root = FindRepositoryRoot();
        string backend = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Platform.Windows/Streaming/StreamWorkerStreamingBackend.cs"));
        string client = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Platform.Windows/Streaming/StreamWorkerNamedPipeClient.cs"));
        string workerMain = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.StreamWorker/src/main.cpp"));

        Assert.DoesNotContain("Encoders: [\"fake\"]", backend, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureMethods: [\"fake\"]", backend, StringComparison.Ordinal);
        Assert.Contains("WorkerCapabilities", client, StringComparison.Ordinal);
        Assert.Contains("WorkerInstanceId.Equals(workerInstanceId)", client, StringComparison.Ordinal);
        Assert.Contains("host.capabilities()", workerMain, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionCompositionHasOneWindowsWorkerRuntime()
    {
        string root = FindRepositoryRoot();
        string registration = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Server/Hosting/BeaconServiceRegistration.cs"));
        string settings = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Server/appsettings.json"));
        string developmentSettings = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Server/appsettings.Development.json"));

        Assert.False(File.Exists(ToPlatformPath(
            root,
            "src/Beacon.Server/Hosting/BeaconStreamingMode.cs")));
        Assert.DoesNotContain("StreamingMode", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("HostModeConfigurationKey", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("HostModeEnvironmentVariable", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("FakeStreamingBackend", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("AddFakeHostBoundaries", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("NoOpClientInputSink", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("StaticGameLibraryProvider", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Dispatch\"", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("HostMode", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("StreamingMode", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("HostMode", developmentSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("StreamingMode", developmentSettings, StringComparison.Ordinal);
        Assert.Contains("AddWindowsHostBoundaries", registration, StringComparison.Ordinal);
        Assert.Contains("StreamWorkerStreamingBackend", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionAcceptanceUsesProductionSecurityPolicy()
    {
        string root = FindRepositoryRoot();
        string acceptance = File.ReadAllText(ToPlatformPath(
            root,
            "tests/Beacon.ProductionAcceptance/Program.cs"));
        string stageServer = File.ReadAllText(ToPlatformPath(
            root,
            "scripts/start-emulator-stage-server.ps1"));

        Assert.DoesNotContain("Security__TestHost", acceptance, StringComparison.Ordinal);
        Assert.DoesNotContain("Security:TestHost", acceptance, StringComparison.Ordinal);
        Assert.DoesNotContain("Security__TestHost", stageServer, StringComparison.Ordinal);
        Assert.DoesNotContain("Security:TestHost", stageServer, StringComparison.Ordinal);
        Assert.Contains(
            "(Join-Path $StateDirectory \"profiles.json\")",
            stageServer,
            StringComparison.Ordinal);
        Assert.Contains(
            "$env:Beacon__Displays__NameMapPath = Join-Path $StateDirectory \"display-name-map.json\"",
            stageServer,
            StringComparison.Ordinal);
        Assert.Contains(
            "new AuthenticationHeaderValue(\"Beacon\", credential)",
            acceptance,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionAcceptanceSeedsRegistrationAndPreparesDisplayBeforeEmulatorWork()
    {
        string root = FindRepositoryRoot();
        string acceptance = File.ReadAllText(ToPlatformPath(
            root,
            "tests/Beacon.ProductionAcceptance/Program.cs"));

        Assert.Contains(
            "WriteRegisteredProfile(profilePath, clientId);",
            acceptance,
            StringComparison.Ordinal);
        int preparedDisplay = acceptance.IndexOf(
            "BEACON_GATE5_STAGE prepared-display",
            StringComparison.Ordinal);
        int emulatorEnvironment = acceptance.IndexOf(
            "BEACON_GATE5_STAGE emulator-environment",
            StringComparison.Ordinal);
        int certifiedBenchmark = acceptance.IndexOf(
            "BEACON_GATE5_STAGE certified-benchmark",
            StringComparison.Ordinal);

        Assert.True(preparedDisplay >= 0, "The production gate has no prepared-display stage.");
        Assert.True(
            emulatorEnvironment > preparedDisplay,
            "The production gate must validate its display before mutating the emulator.");
        Assert.True(
            certifiedBenchmark > emulatorEnvironment,
            "The production gate must validate the emulator environment before benchmarking.");
        Assert.Contains(
            "IReadOnlySet<string> activeVirtualDisplaysBefore = CaptureActiveVirtualDisplayNames();",
            acceptance,
            StringComparison.Ordinal);
        Assert.Contains(
            "ResolvePreparedDisplayName(activeVirtualDisplaysBefore)",
            acceptance,
            StringComparison.Ordinal);
        Assert.Contains(
            "ValidateDisplayNameReleased(preparedDisplayName);",
            acceptance,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ResolveMappedDisplayName(displayMapPath",
            acceptance,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Gate5TransportFailureReleasesTheSurfaceEvidenceWaiter()
    {
        string root = FindRepositoryRoot();
        string acceptance = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Android/app/src/androidTest/java/dev/beacon/android/BeaconStreamCoreInstrumentationTest.java"));

        Assert.Contains(
            "videoRuntime.recordStreamFailure(stage);",
            acceptance,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (activity.isDestroyed())",
            acceptance,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TestDoublesAreNotCompiledIntoProductionProjects()
    {
        string root = FindRepositoryRoot();
        string[] productionTestDoubleFiles =
        [
            "src/Beacon.Core/Benchmarks/FakeBenchmarkRuntime.cs",
            "src/Beacon.Core/Displays/FakeDisplayBackend.cs",
            "src/Beacon.Core/Games/FakeGameLauncher.cs",
            "src/Beacon.Core/Recovery/FakeRecoveryBackend.cs",
            "src/Beacon.Core/Sessions/FakeSessionActivityInspector.cs",
            "src/Beacon.Core/Streaming/FakeStreamingBackend.cs"
        ];

        Assert.All(productionTestDoubleFiles, relative =>
            Assert.False(File.Exists(ToPlatformPath(root, relative)), relative));

        string input = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Core/Input/ClientInput.cs"));
        string authorization = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Core/Streaming/IStreamSessionAuthorizer.cs"));
        string hostOptions = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.Server/Hosting/BeaconHostOptions.cs"));

        Assert.DoesNotContain("NoOpClientInputSink", input, StringComparison.Ordinal);
        Assert.DoesNotContain("FakeStreamSessionAuthorizer", authorization, StringComparison.Ordinal);
        Assert.DoesNotContain("Fake", hostOptions, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityGuardUsesOneTrackedDefinitionManifest()
    {
        string root = FindRepositoryRoot();
        string manifest = ToPlatformPath(root, "contracts/gate3_architecture_guard.json");

        Assert.True(File.Exists(manifest), manifest);
        Assert.Contains(
            "contracts/gate3_architecture_guard.json",
            File.ReadAllText(ToPlatformPath(root, "scripts/test-gate3.ps1")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeAndTestsContainNoCompatibilityPathsOrTokens()
    {
        string root = FindRepositoryRoot();
        foreach (string relativeRoot in Guard.ForbiddenDirectories)
        {
            Assert.False(Directory.Exists(ToPlatformPath(root, relativeRoot)), relativeRoot);
        }

        var matches = new List<string>();
        foreach (string file in await EnumerateTrackedSourceFilesAsync(root))
        {
            if (CompatibilityPattern.IsMatch(Path.GetFileName(file))
                || CompatibilityPattern.IsMatch(await File.ReadAllTextAsync(file)))
            {
                matches.Add(ToRepositoryRelativePath(root, file));
            }
        }

        Assert.Empty(matches.Order());
    }

    [Theory]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData("Beacon.slnx")]
    [InlineData("Directory.Build.props")]
    [InlineData("scripts/build-native.sh")]
    [InlineData("src/Beacon.ClientLab/index.html")]
    [InlineData("src/Beacon.ClientLab/styles.css")]
    [InlineData("src/Beacon.Android/gradle.properties")]
    [InlineData("src/Beacon.Cockpit/MainWindow.xaml")]
    [InlineData("contracts/media_datagram_v1.md")]
    [InlineData("README.md")]
    public void CompatibilityGuardCoversTrackedSourceBuildScriptAndCiFiles(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);

        Assert.True(IsTrackedScanCandidate(normalized), normalized);
    }

    [Theory]
    [InlineData("WebRTC")]
    [InlineData("web_rtc")]
    [InlineData("HTTP-media")]
    [InlineData("http media")]
    [InlineData("HttpMedia")]
    [InlineData("MediaOverHttp")]
    public void CompatibilityGuardRejectsUnselectedMediaAliases(string alias)
    {
        Assert.Matches(CompatibilityPattern, alias);
    }

    [Fact]
    public void TrackedFileScanIgnoresWorkingTreeDeletions()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"beacon-architecture-deletion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Empty(ResolveExistingTrackedFiles(root, ["src/deleted.cs"]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NativeStreamingApisRemainInsideSelectedWorkerAndStreamCoreBoundaries()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (string file in await EnumerateTrackedSourceFilesAsync(root))
        {
            if (Path.GetExtension(file).Equals(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relativePath = ToRepositoryRelativePath(root, file);
            string contents = await File.ReadAllTextAsync(file);
            foreach (NativeBoundaryRule rule in NativeBoundaryRules)
            {
                if (rule.Pattern.IsMatch(Path.GetFileName(file)) || rule.Pattern.IsMatch(contents))
                {
                    bool allowed = rule.AllowedPrefixes.Any(prefix =>
                        relativePath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
                    if (!allowed)
                    {
                        violations.Add($"{rule.Name}: {relativePath}");
                    }
                }
            }
        }

        Assert.Empty(violations.Order());
    }

    [Fact]
    public void NativeTestsDoNotUseCrashDialogAssertions()
    {
        string root = FindRepositoryRoot();
        string[] nativeTestRoots =
        [
            ToPlatformPath(root, "tests/Beacon.StreamProtocol.Tests"),
            ToPlatformPath(root, "tests/Beacon.StreamWorker.Tests"),
            ToPlatformPath(root, "src/Beacon.Android/app/src/main/cpp/streamcore/tests")
        ];
        var crashAssertion = new Regex(
            @"\b(?:assert|abort)\s*\(",
            RegexOptions.CultureInvariant);

        string[] violations = nativeTestRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".cpp" or ".h")
            .Where(path => crashAssertion.IsMatch(File.ReadAllText(path)))
            .Select(path => ToRepositoryRelativePath(root, path))
            .Order()
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void NativeTestFailureConsumersUseTheUnwindingRunner()
    {
        string root = FindRepositoryRoot();
        string[] nativeTestRoots =
        [
            ToPlatformPath(root, "tests/Beacon.StreamProtocol.Tests"),
            ToPlatformPath(root, "src/Beacon.Android/app/src/main/cpp/streamcore/tests")
        ];

        string[] violations = nativeTestRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*.cpp", SearchOption.AllDirectories))
            .Where(path => File.ReadAllText(path).Contains("#include \"test_failure.h\"", StringComparison.Ordinal))
            .Where(path => !File.ReadAllText(path).Contains("testing::run_tests(", StringComparison.Ordinal))
            .Select(path => ToRepositoryRelativePath(root, path))
            .Order()
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ProductionQuicListenerDoesNotInjectSyntheticMedia()
    {
        string root = FindRepositoryRoot();
        string listener = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.StreamWorker/src/quic_listener.cpp"));

        Assert.DoesNotContain("SyntheticMediaSource", listener, StringComparison.Ordinal);
        Assert.DoesNotContain("emit_access_unit_marker", listener, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic_media_source_", listener, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionWorkerContainsNoSyntheticMediaRoute()
    {
        string root = FindRepositoryRoot();
        string workerRoot = ToPlatformPath(root, "src/Beacon.StreamWorker");

        string[] violations = Directory
            .EnumerateFiles(workerRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Contains(
                "synthetic_media", StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText(path).Contains("SyntheticMediaSource", StringComparison.Ordinal)
                || File.ReadAllText(path).Contains("emit_access_unit_marker", StringComparison.Ordinal))
            .Select(path => ToRepositoryRelativePath(root, path))
            .Order()
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void PortableWorkerCoreExcludesWindowsMediaAndControlPrimitives()
    {
        string root = FindRepositoryRoot();
        string host = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.StreamWorker/include/beacon/worker/worker_host.h"));
        string build = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.StreamWorker/CMakeLists.txt"));

        Assert.Contains(
            "beacon/worker/video/worker_video_capabilities.h",
            host,
            StringComparison.Ordinal);
        Assert.DoesNotContain("production_video_capabilities.h", host, StringComparison.Ordinal);
        Assert.DoesNotContain("wgc_display_capture.h", host, StringComparison.Ordinal);
        Assert.DoesNotContain("nvenc_h264_encoder.h", host, StringComparison.Ordinal);

        int portableStart = build.IndexOf(
            "add_library(\n  BeaconStreamWorkerPortableCore",
            StringComparison.Ordinal);
        int windowsStart = build.IndexOf("if(WIN32)", StringComparison.Ordinal);
        Assert.True(portableStart >= 0 && windowsStart > portableStart);
        string portable = build[portableStart..windowsStart];
        Assert.Contains("src/benchmark_source.cpp", portable, StringComparison.Ordinal);
        Assert.Contains("src/quic_listener.cpp", portable, StringComparison.Ordinal);
        Assert.Contains("src/worker_host.cpp", portable, StringComparison.Ordinal);
        Assert.Contains("src/worker_ipc_frame.cpp", portable, StringComparison.Ordinal);
        Assert.DoesNotContain("src/named_pipe_channel.cpp", portable, StringComparison.Ordinal);
        Assert.DoesNotContain("src/capture/", portable, StringComparison.Ordinal);
        Assert.DoesNotContain("src/video/d3d11", portable, StringComparison.Ordinal);
        Assert.DoesNotContain("src/video/nvenc", portable, StringComparison.Ordinal);
        Assert.DoesNotContain("src/video/production_video", portable, StringComparison.Ordinal);
        Assert.Contains(
            "Beacon::StreamWorkerPortableCore",
            build[windowsStart..],
            StringComparison.Ordinal);
    }

    [Fact]
    public void QuicListenerUsesOnePortablePkcs12IdentityContract()
    {
        string root = FindRepositoryRoot();
        string header = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.StreamWorker/include/beacon/worker/quic_listener.h"));
        string source = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.StreamWorker/src/quic_listener.cpp"));

        Assert.Contains("#include <filesystem>", header, StringComparison.Ordinal);
        Assert.Contains("std::filesystem::path identity_path", header, StringComparison.Ordinal);
        Assert.Contains("#if defined(_WIN32)", source, StringComparison.Ordinal);
        Assert.Contains("QUIC_CREDENTIAL_TYPE_CERTIFICATE_CONTEXT", source, StringComparison.Ordinal);
        Assert.Contains("QUIC_CREDENTIAL_TYPE_CERTIFICATE_PKCS12", source, StringComparison.Ordinal);
        Assert.Contains("credentials.CertificatePkcs12", source, StringComparison.Ordinal);
        Assert.Contains("stream::secure_clear_bytes(identity_bytes_)", source, StringComparison.Ordinal);
        Assert.Contains("std::filesystem::path identity_path_", source, StringComparison.Ordinal);

        int windowsIncludes = source.IndexOf("#include <Windows.h>", StringComparison.Ordinal);
        int windowsGuard = source.LastIndexOf(
            "#if defined(_WIN32)",
            windowsIncludes,
            StringComparison.Ordinal);
        int windowsGuardEnd = source.IndexOf("#endif", windowsIncludes, StringComparison.Ordinal);
        Assert.True(windowsGuard >= 0 && windowsGuard < windowsIncludes);
        Assert.True(windowsGuardEnd > windowsIncludes);
    }

    [Fact]
    public void HostedBenchmarkWorkerRemainsLinuxTestInfrastructureOnly()
    {
        string root = FindRepositoryRoot();
        string testCmake = File.ReadAllText(ToPlatformPath(
            root,
            "tests/Beacon.StreamProtocol.Tests/CMakeLists.txt"));
        string productionWorkerCmake = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.StreamWorker/CMakeLists.txt"));
        string nativeCmake = File.ReadAllText(ToPlatformPath(root, "native/CMakeLists.txt"));
        string hostedSource = File.ReadAllText(ToPlatformPath(
            root,
            "tests/Beacon.StreamProtocol.Tests/hosted_benchmark_worker.cpp"));
        string hostedChannel = File.ReadAllText(ToPlatformPath(
            root,
            "tests/Beacon.StreamProtocol.Tests/hosted_benchmark_worker_channel.cpp"));

        int linuxTestGuard = testCmake.IndexOf("if(UNIX AND NOT ANDROID)", StringComparison.Ordinal);
        int hostedTarget = testCmake.IndexOf(
            "add_executable(\n      BeaconHostedBenchmarkWorker",
            StringComparison.Ordinal);
        Assert.True(linuxTestGuard >= 0 && hostedTarget > linuxTestGuard);
        Assert.DoesNotContain("BeaconHostedBenchmarkWorker", productionWorkerCmake, StringComparison.Ordinal);
        Assert.DoesNotContain("BeaconHostedBenchmarkWorker", nativeCmake, StringComparison.Ordinal);
        Assert.Contains("BEACON_HOSTED_WORKER_READY", hostedSource, StringComparison.Ordinal);
        Assert.Contains("BEACON_HOSTED_WORKER_STOPPED", hostedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("sleep_for", hostedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("wait_for", hostedSource, StringComparison.Ordinal);
        Assert.Contains("::poll(descriptors.data(), descriptors.size(), -1)", hostedChannel, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionWorkerCallbacksDoNotCaptureShorterLivedObjectsByReference()
    {
        string root = FindRepositoryRoot();
        string main = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.StreamWorker/src/main.cpp"));

        Assert.DoesNotContain("[&enqueue_async]", main, StringComparison.Ordinal);
        Assert.DoesNotContain("[&video_pipeline]", main, StringComparison.Ordinal);
        Assert.Contains("std::weak_ptr", main, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoPipelineDelegatesExternalGenerationStartupOutOfItsStateTransition()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(ToPlatformPath(
            root,
            "src/Beacon.StreamWorker/src/video/worker_video_pipeline.cpp"));
        int start = source.IndexOf(
            "void WorkerVideoPipeline::start_generation",
            StringComparison.Ordinal);
        int stop = source.IndexOf(
            "void WorkerVideoPipeline::stop_generation",
            StringComparison.Ordinal);

        Assert.True(start >= 0 && stop > start);
        string transition = source[start..stop];
        Assert.DoesNotContain("factory_.create", transition, StringComparison.Ordinal);
        Assert.DoesNotContain("->start(", transition, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<string>> EnumerateTrackedSourceFilesAsync(string root)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(root);
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("-z");
        startInfo.ArgumentList.Add("--cached");
        startInfo.ArgumentList.Add("--others");
        startInfo.ArgumentList.Add("--exclude-standard");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start git tracked-file scan.");
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git ls-files failed with exit code {process.ExitCode}: {await error}");
        }

        IEnumerable<string> trackedPaths = (await output)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeRelativePath)
            .Where(IsTrackedScanCandidate)
            .Where(path => !Guard.ExcludedPaths.Contains(path))
            .Where(path => !HasGeneratedSegment(path));
        return ResolveExistingTrackedFiles(root, trackedPaths);
    }

    private static IReadOnlyList<string> ResolveExistingTrackedFiles(
        string root,
        IEnumerable<string> relativePaths) =>
        relativePaths
            .Select(path => ToPlatformPath(root, path))
            .Where(File.Exists)
            .ToArray();

    private static bool IsTrackedScanCandidate(string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        string firstSegment = normalized.Split('/', 2)[0];
        bool inScannedTree = Guard.ScannedRoots.Contains(firstSegment);
        bool scannedRootFile = !normalized.Contains('/') && Guard.ScannedRootFiles.Contains(normalized);
        return (inScannedTree || scannedRootFile)
            && Guard.ScannedExtensions.Contains(Path.GetExtension(normalized));
    }

    private static bool HasGeneratedSegment(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("build", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("coverage", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("dist", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("out", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("_deps", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".cxx", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".gradle", StringComparison.OrdinalIgnoreCase));
    }

    private static string ToRepositoryRelativePath(string root, string path) =>
        NormalizeRelativePath(Path.GetRelativePath(root, path));

    private static string NormalizeRelativePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string ToPlatformPath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Beacon.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static GuardDefinition LoadGuardDefinition()
    {
        string path = ToPlatformPath(
            FindRepositoryRoot(),
            "contracts/gate3_architecture_guard.json");
        GuardManifest manifest = JsonSerializer.Deserialize<GuardManifest>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Gate 3 architecture guard manifest is empty.");
        string[] forbiddenDirectories = JoinFragments(
            manifest.ForbiddenDirectoryFragments,
            nameof(manifest.ForbiddenDirectoryFragments));
        string[] literalPatterns = JoinFragments(
                manifest.LiteralTokenFragments,
                nameof(manifest.LiteralTokenFragments))
            .Select(Regex.Escape)
            .ToArray();
        string[] regexPatterns = JoinFragments(
            manifest.RegexTokenFragments,
            nameof(manifest.RegexTokenFragments));

        return new GuardDefinition(
            new HashSet<string>(RequireValues(manifest.ScannedRoots, nameof(manifest.ScannedRoots)),
                StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(RequireValues(
                manifest.ScannedRootFiles,
                nameof(manifest.ScannedRootFiles)), StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(RequireValues(
                manifest.ScannedExtensions,
                nameof(manifest.ScannedExtensions)), StringComparer.OrdinalIgnoreCase),
            forbiddenDirectories,
            new HashSet<string>(RequireValues(
                manifest.ExcludedPaths,
                nameof(manifest.ExcludedPaths)), StringComparer.OrdinalIgnoreCase),
            new Regex(
                string.Join('|', literalPatterns.Concat(regexPatterns)),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    private static string[] JoinFragments(string[][] fragments, string name) =>
        RequireValues(fragments, name)
            .Select((parts, index) => string.Concat(RequireValues(parts, $"{name}[{index}]")))
            .ToArray();

    private static T[] RequireValues<T>(T[]? values, string name) =>
        values is { Length: > 0 }
            ? values
            : throw new InvalidDataException($"Gate 3 architecture guard '{name}' is empty.");

    private sealed record GuardDefinition(
        HashSet<string> ScannedRoots,
        HashSet<string> ScannedRootFiles,
        HashSet<string> ScannedExtensions,
        IReadOnlyList<string> ForbiddenDirectories,
        HashSet<string> ExcludedPaths,
        Regex CompatibilityPattern);

    private sealed record GuardManifest(
        string[] ScannedRoots,
        string[] ScannedRootFiles,
        string[] ScannedExtensions,
        string[][] ForbiddenDirectoryFragments,
        string[][] LiteralTokenFragments,
        string[][] RegexTokenFragments,
        string[] ExcludedPaths);

    private sealed record NativeBoundaryRule(
        string Name,
        Regex Pattern,
        IReadOnlyList<string> AllowedPrefixes);
}
