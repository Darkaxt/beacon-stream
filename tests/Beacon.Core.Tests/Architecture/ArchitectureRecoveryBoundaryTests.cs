using System.Diagnostics;
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

    private static readonly string[] ForbiddenDirectories =
    [
        "src/Beacon.StreamingProbe",
        "tests/Beacon.StreamingProbe.Tests",
        "src/Beacon.Android/streaming-moonlight"
    ];

    private static readonly string[] ScannedRoots =
    [
        ".github",
        "contracts",
        "native",
        "scripts",
        "src",
        "tests"
    ];

    private static readonly HashSet<string> ScannedRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Beacon.slnx",
        "CMakeLists.txt",
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "README.md",
        "build.gradle",
        "build.gradle.kts",
        "global.json",
        "gradle.properties",
        "package-lock.json",
        "package.json",
        "settings.gradle",
        "settings.gradle.kts"
    };

    private static readonly HashSet<string> ScannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c",
        ".bat",
        ".cmake",
        ".cmd",
        ".cpp",
        ".cs",
        ".csproj",
        ".css",
        ".gradle",
        ".h",
        ".html",
        ".java",
        ".json",
        ".kt",
        ".kts",
        ".mjs",
        ".md",
        ".ps1",
        ".properties",
        ".props",
        ".proto",
        ".sh",
        ".slnx",
        ".targets",
        ".ts",
        ".tsx",
        ".txt",
        ".yml",
        ".yaml",
        ".xaml",
        ".xml"
    };

    private static readonly Regex CompatibilityPattern = new(
        @"Apollo|Sunshine|Moonlight|GameStream|Web[_-]?RTC|HTTP[-_ ]?media|MediaOverHttp|" +
        @"\b(?:RTSP|RTP)\b|ExternalProcessStreaming|StreamingWrapper|WrapperChild|RuntimeDescriptor|" +
        "LaunchUri|nativeSession",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
                "tests/Beacon.StreamWorker.Tests"
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
    public async Task RuntimeAndTestsContainNoCompatibilityPathsOrTokens()
    {
        string root = FindRepositoryRoot();
        foreach (string relativeRoot in ForbiddenDirectories)
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
            ToPlatformPath(root, "tests/Beacon.StreamWorker.Tests")
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
            .Where(path => !path.Equals(
                "tests/Beacon.Core.Tests/Architecture/ArchitectureRecoveryBoundaryTests.cs",
                StringComparison.OrdinalIgnoreCase))
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
        bool inScannedTree = ScannedRoots.Contains(firstSegment, StringComparer.OrdinalIgnoreCase);
        bool scannedRootFile = !normalized.Contains('/') && ScannedRootFiles.Contains(normalized);
        return (inScannedTree || scannedRootFile)
            && ScannedExtensions.Contains(Path.GetExtension(normalized));
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

    private sealed record NativeBoundaryRule(
        string Name,
        Regex Pattern,
        IReadOnlyList<string> AllowedPrefixes);
}
