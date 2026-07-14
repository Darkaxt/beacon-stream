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
