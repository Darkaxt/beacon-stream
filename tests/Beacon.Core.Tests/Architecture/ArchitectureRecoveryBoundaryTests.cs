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
        "contracts",
        "native",
        "scripts",
        "src",
        "tests"
    ];

    private static readonly HashSet<string> ScannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c",
        ".cmake",
        ".cpp",
        ".cs",
        ".csproj",
        ".gradle",
        ".h",
        ".java",
        ".json",
        ".kt",
        ".ps1",
        ".proto",
        ".ts",
        ".tsx",
        ".txt",
        ".yml",
        ".yaml",
        ".xml"
    };

    private static readonly Regex CompatibilityPattern = new(
        "Apollo|Sunshine|Moonlight|GameStream|RTSP|RTP|ExternalProcessStreaming|StreamingWrapper|" +
        "WrapperChild|RuntimeDescriptor|LaunchUri|nativeSession",
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
    public void RuntimeAndTestsContainNoCompatibilityPathsOrTokens()
    {
        string root = FindRepositoryRoot();
        foreach (string relativeRoot in ForbiddenDirectories)
        {
            Assert.False(Directory.Exists(ToPlatformPath(root, relativeRoot)), relativeRoot);
        }

        var matches = new List<string>();
        foreach (string relativeRoot in ScannedRoots)
        {
            string path = ToPlatformPath(root, relativeRoot);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (string file in EnumerateSourceFiles(path))
            {
                if (CompatibilityPattern.IsMatch(Path.GetFileName(file))
                    || CompatibilityPattern.IsMatch(File.ReadAllText(file)))
                {
                    matches.Add(ToRepositoryRelativePath(root, file));
                }
            }
        }

        Assert.Empty(matches.Order());
    }

    [Fact]
    public void NativeStreamingApisRemainInsideSelectedWorkerAndStreamCoreBoundaries()
    {
        string root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (string relativeRoot in ScannedRoots)
        {
            string path = ToPlatformPath(root, relativeRoot);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (string file in EnumerateSourceFiles(path))
            {
                string relativePath = ToRepositoryRelativePath(root, file);
                string contents = File.ReadAllText(file);
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
        }

        Assert.Empty(violations.Order());
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => ScannedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !path.EndsWith(
                "ArchitectureRecoveryBoundaryTests.cs",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !HasGeneratedSegment(path));

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
