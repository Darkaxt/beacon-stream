using System.Text.RegularExpressions;

namespace Beacon.Core.Tests.Docs;

public sealed class ReadmeLinkTests
{
    [Fact]
    public void ReadmeLocalMarkdownReferencesExist()
    {
        string root = FindRepositoryRoot();
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));
        MatchCollection matches = Regex.Matches(readme, "`(?<path>[^`]+\\.md)`");

        string[] missing = matches
            .Select(match => match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar))
            .Where(path => !Path.IsPathRooted(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !File.Exists(Path.Combine(root, path)))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void ReadmeReferencesApprovedNativeStreamingAuditAndPlan()
    {
        string root = FindRepositoryRoot();
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains(
            "docs/source-audits/2026-07-10-beacon-streamworker-streamcore.md",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "docs/superpowers/plans/2026-07-10-beacon-stream-gates-3-5.md",
            readme,
            StringComparison.Ordinal);
    }

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
