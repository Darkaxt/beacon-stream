# Milestone 33 README Link Integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent README documentation references from drifting to non-existent local markdown files.

**Architecture:** Add a deterministic xUnit docs test that extracts markdown file paths inside README backticks and verifies each referenced local `.md` path exists. Fix the currently broken Milestone 14 link.

**Tech Stack:** .NET, xUnit, regular expressions.

---

### Task 1: README Link Guard

**Files:**
- Create: `tests/Beacon.Core.Tests/Docs/ReadmeLinkTests.cs`
- Modify: `README.md`

- [x] **Step 1: Write the failing docs test**

Create `ReadmeLinkTests`:

```csharp
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

        var missing = matches
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
```

- [x] **Step 2: Run focused test to verify failure**

Run:

```powershell
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter ReadmeLocalMarkdownReferencesExist
```

Expected: FAIL because README references `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-14-selected-client-admin.md`, which does not exist.

- [x] **Step 3: Fix the broken README link**

Replace:

```markdown
`docs/superpowers/plans/2026-07-08-beacon-stream-milestone-14-selected-client-admin.md`
```

with:

```markdown
`docs/superpowers/plans/2026-07-08-beacon-stream-milestone-14-recovery-contract.md`
```

- [x] **Step 4: Run focused test to verify pass**

Run the same focused test. Expected: PASS.

### Task 2: Validation And Sync

**Files:**
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-33-readme-link-integrity.md`

- [x] **Step 1: Run full validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
dotnet test Beacon.slnx
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright lint
pnpm --dir tests\Beacon.ClientLab.Playwright test
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
```

Expected: all build/test/lint commands pass. The timeout-pattern audit should return no matches.

- [ ] **Step 2: Commit and sync**

Commit, push, open a pull request, wait for CI, and merge only after CI is green.
