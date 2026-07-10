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

    private static readonly string[] CompatibilityRoots =
    [
        "src/Beacon.Platform.Windows/Streaming",
        "src/Beacon.StreamingProbe",
        "tests/Beacon.StreamingProbe.Tests",
        "src/Beacon.Android/streaming-moonlight"
    ];

    private static readonly string[] ScannedRoots =
    [
        "src/Beacon.Core",
        "src/Beacon.Server",
        "src/Beacon.Cockpit",
        "src/Beacon.Android/app/src",
        "tests/Beacon.Core.Tests",
        "tests/Beacon.Server.Tests",
        "tests/Beacon.Cockpit.Tests",
        "tests/Beacon.Platform.Windows.Tests"
    ];

    private static readonly HashSet<string> ScannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c",
        ".cpp",
        ".cs",
        ".csproj",
        ".gradle",
        ".h",
        ".java",
        ".json",
        ".kt",
        ".txt",
        ".xml"
    };

    private static readonly Regex CompatibilityPattern = new(
        "Moonlight|GameStream|Rtsp|Rtp|ExternalProcess|Wrapper|RuntimeDescriptor|" +
        "LaunchUri|nativeSession|beacon-test|BeaconTest",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
    public void CompatibilityDebtMatchesTheReviewedSnapshot()
    {
        string root = FindRepositoryRoot();
        string snapshotPath = Path.Combine(
            root,
            "tests/Beacon.Core.Tests/Architecture/architecture-recovery-debt.txt");
        Assert.True(File.Exists(snapshotPath), "The reviewed architecture debt snapshot is missing.");

        var expected = File.ReadAllLines(snapshotPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(NormalizeRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> actual = FindCompatibilityDebt(root);

        string[] added = actual.Except(expected, StringComparer.OrdinalIgnoreCase).Order().ToArray();
        string[] removed = expected.Except(actual, StringComparer.OrdinalIgnoreCase).Order().ToArray();

        Assert.True(
            actual.SetEquals(expected),
            $"Architecture debt changed.{Environment.NewLine}" +
            $"Added: {string.Join(", ", added)}{Environment.NewLine}" +
            $"Removed but not acknowledged: {string.Join(", ", removed)}");
    }

    private static HashSet<string> FindCompatibilityDebt(string root)
    {
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string relativeRoot in CompatibilityRoots)
        {
            string path = ToPlatformPath(root, relativeRoot);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (string file in EnumerateSourceFiles(path))
            {
                matches.Add(ToRepositoryRelativePath(root, file));
            }
        }

        AddSubmoduleMarker(root, matches, "src/Beacon.Android/streaming-moonlight/src/main/cpp/mbedtls");
        AddSubmoduleMarker(root, matches, "src/Beacon.Android/streaming-moonlight/src/main/cpp/moonlight-common-c");

        foreach (string relativeRoot in ScannedRoots)
        {
            string path = ToPlatformPath(root, relativeRoot);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (string file in EnumerateSourceFiles(path))
            {
                if (CompatibilityPattern.IsMatch(File.ReadAllText(file)))
                {
                    matches.Add(ToRepositoryRelativePath(root, file));
                }
            }
        }

        return matches;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => ScannedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !path.EndsWith(
                "architecture-recovery-debt.txt",
                StringComparison.OrdinalIgnoreCase))
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
            || segment.Equals(".cxx", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".gradle", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("mbedtls", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("moonlight-common-c", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddSubmoduleMarker(
        string root,
        ISet<string> matches,
        string relativePath)
    {
        if (Directory.Exists(ToPlatformPath(root, relativePath)))
        {
            matches.Add(NormalizeRelativePath(relativePath));
        }
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
}
