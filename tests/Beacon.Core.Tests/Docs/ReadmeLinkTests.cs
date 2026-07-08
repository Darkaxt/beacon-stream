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
