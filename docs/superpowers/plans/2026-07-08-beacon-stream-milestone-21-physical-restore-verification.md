# Physical Restore Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make physical-display restore success mean the physical display is verified primary again, including the no-phone display probe path.

**Architecture:** Keep topology apply/verification in the Windows display backend boundary. The raw Windows API adapter must not report restore success unless a post-apply topology query confirms a physical primary; the backend keeps the existing finite fingerprint reconciliation for stale Windows reports. The display probe should use the backend restore path so manual validation exercises the same verified behavior as server recovery.

**Tech Stack:** .NET 10, C# 14, Windows CCD DisplayConfig APIs, SudoVDA boundary, xUnit.

---

## Requirements Covered

- `REQ-DISP-014`: verify the physical display is primary again after session end or recovery.
- `REQ-DISP-015`: reconcile stale or wrong Windows topology instead of accepting an unverified restore.
- `REQ-DISP-016`: prevent the laptop panel from being stranded inactive when no session owns that state.
- `REQ-DISP-018`: log restore topology decisions with before/after state and reason.
- `REQ-REC-008`: expose selected recovery/restore actions clearly in diagnostics and tools.
- `REQ-TEST-007`: validate display recovery without requiring the real Artemis phone.

## File Map

- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayApi.cs`
  - Verify `RestorePhysicalPrimaryAsync` by querying topology after apply.
  - Return explicit errors when apply succeeds but no physical primary is verified.
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayBackend.cs`
  - Keep finite stale-topology reconciliation, but make repeated unverified restores explain the last topology fingerprint.
- Modify: `src/Beacon.DisplayProbe/DisplayProbeFormatter.cs`
  - Add `FormatRestoreResult` for backend restore results.
- Modify: `src/Beacon.DisplayProbe/Program.cs`
  - Route `restore-physical` through `WindowsDisplayBackend.RestorePhysicalPrimaryAsync`.
- Create: `src/Beacon.DisplayProbe/DisplayProbeApp.cs`
  - Provide an injectable runner so command routing can be tested without real monitor changes.
- Modify: `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayBackendTests.cs`
  - Add explicit failure coverage for repeated unverified restore.
- Create: `tests/Beacon.DisplayProbe.Tests/DisplayProbeAppTests.cs`
  - Verify `restore-physical` uses the backend restore path.
- Modify: `tests/Beacon.DisplayProbe.Tests/DisplayProbeFormatterTests.cs`
  - Cover the restore-result formatter.
- Modify: `docs/windows-display-backend.md`
  - Document that `restore-physical` is verified and can fail after an apparently successful Windows apply.
- Modify: `README.md`
  - Mention verified physical-primary recovery in the display lifecycle summary.
- Modify: this plan file
  - Track execution state.

## Task 1: Backend Restore Failure Contract

**Files:**
- Modify: `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayBackendTests.cs`
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayBackend.cs`

- [x] **Step 1: Write the failing backend test**

Add this test to `WindowsDisplayBackendTests`:

```csharp
[Fact]
public async Task RestorePhysicalPrimaryAsync_WhenTopologyRepeatsWithoutPhysicalPrimary_FailsWithDiagnostic()
{
    var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
    api.RestoreTopologies.Enqueue(DisplayTopologySnapshot.Extended(
        physicalDisplayId: "physical-laptop-panel",
        virtualDisplayId: "client-z-fold-7",
        width: 2560,
        height: 1600,
        refreshHz: 120,
        virtualPrimary: true));
    api.RestoreTopologies.Enqueue(DisplayTopologySnapshot.Extended(
        physicalDisplayId: "physical-laptop-panel",
        virtualDisplayId: "client-z-fold-7",
        width: 2560,
        height: 1600,
        refreshHz: 120,
        virtualPrimary: true));
    var backend = new WindowsDisplayBackend(api);

    DisplayRestoreResult result = await backend.RestorePhysicalPrimaryAsync(CancellationToken.None);

    Assert.False(result.Success);
    Assert.Contains("not verified", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("physical-laptop-panel", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(2, api.RestoreRequests.Count);
    DisplayOperationLogEntry entry = Assert.Single(backend.OperationLog);
    Assert.False(entry.Primary);
    Assert.Contains("restore-verification-failed", entry.Reason);
}
```

- [x] **Step 2: Run the test to verify red**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter RestorePhysicalPrimaryAsync_WhenTopologyRepeatsWithoutPhysicalPrimary_FailsWithDiagnostic
```

Expected: fail because the error does not include the repeated topology fingerprint/display evidence.

- [x] **Step 3: Implement the minimal backend diagnostic improvement**

In `WindowsDisplayBackend.RestorePhysicalPrimaryAsync`, when `seenUnverifiedTopologies.Add(topology.Fingerprint)` returns false, return:

```csharp
DisplayRestoreResult fail = DisplayRestoreResult.Fail(
    $"Physical primary restore was not verified after topology reconciliation. LastTopology={topology.Fingerprint}.");
LogRestore(before, topology, fail, "restore-verification-failed");
return fail;
```

- [x] **Step 4: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter RestorePhysicalPrimaryAsync_WhenTopologyRepeatsWithoutPhysicalPrimary_FailsWithDiagnostic
```

Expected: test passes.

## Task 2: Raw Windows Restore Must Verify

**Files:**
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayApi.cs`
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayDiagnostics.cs`
- Modify: `tests/Beacon.Platform.Windows.Tests/Displays/DisplayTopologySnapshotTests.cs`
- Modify: `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayDiagnosticsTests.cs`

- [x] **Step 1: Write a focused snapshot test for physical-primary evidence**

Add to `DisplayTopologySnapshotTests`:

```csharp
[Fact]
public void PhysicalPrimaryVerifiedRequiresPhysicalDisplayAtPrimary()
{
    DisplayTopologySnapshot topology = DisplayTopologySnapshot.Extended(
        physicalDisplayId: "physical-laptop-panel",
        virtualDisplayId: "client-z-fold-7",
        width: 2560,
        height: 1600,
        refreshHz: 120,
        virtualPrimary: true);

    Assert.False(topology.PhysicalPrimaryVerified);
}
```

- [x] **Step 2: Run the snapshot test**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter PhysicalPrimaryVerifiedRequiresPhysicalDisplayAtPrimary
```

Expected: pass, confirming the verification predicate already models the required postcondition.

- [x] **Step 3: Verify raw Windows restore after apply**

In `WindowsDisplayApi.RestorePhysicalPrimaryAsync`, after `TrySetPrimaryDisplay(displayName, out string diagnostic)` succeeds, query active topology and require `PhysicalPrimaryVerified`:

```csharp
if (!TrySetPrimaryDisplay(displayName, out string diagnostic))
{
    return Task.FromResult(DisplayApiResult.Fail(diagnostic));
}

DisplayTopologySnapshot verified = QueryActiveTopology();
if (!verified.PhysicalPrimaryVerified)
{
    return Task.FromResult(DisplayApiResult.Fail(
        $"DisplayConfig apply reported success for {displayName}, but physical primary was not verified. LastTopology={verified.Fingerprint}."));
}

return Task.FromResult(DisplayApiResult.Ok());
```

Keep the existing physical candidate selection and fallback logic unchanged.

- [x] **Step 4: Run platform display tests**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter "DisplayTopologySnapshotTests|WindowsDisplayBackendTests|WindowsDisplayRestoreCandidateTests"
```

Expected: all targeted platform display tests pass.

## Task 3: Probe Uses Verified Restore

**Files:**
- Modify: `src/Beacon.DisplayProbe/DisplayProbeFormatter.cs`
- Create: `src/Beacon.DisplayProbe/DisplayProbeApp.cs`
- Modify: `src/Beacon.DisplayProbe/Program.cs`
- Modify: `tests/Beacon.DisplayProbe.Tests/DisplayProbeFormatterTests.cs`
- Create: `tests/Beacon.DisplayProbe.Tests/DisplayProbeAppTests.cs`

- [x] **Step 1: Write restore formatter tests**

Add to `DisplayProbeFormatterTests`:

```csharp
[Fact]
public void FormatRestoreResultReportsVerifiedSuccess()
{
    string output = DisplayProbeFormatter.FormatRestoreResult(DisplayRestoreResult.Ok());

    Assert.Equal("restore-physical: success verified=True", output);
}

[Fact]
public void FormatRestoreResultReportsVerificationFailure()
{
    string output = DisplayProbeFormatter.FormatRestoreResult(
        DisplayRestoreResult.Fail("physical primary was not verified"));

    Assert.Contains("restore-physical: failed", output);
    Assert.Contains("physical primary was not verified", output);
}
```

- [x] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.DisplayProbe.Tests/Beacon.DisplayProbe.Tests.csproj --filter FormatRestoreResult
```

Expected: compile failure because `FormatRestoreResult` does not exist.

- [x] **Step 3: Add restore formatter**

In `DisplayProbeFormatter`, add:

```csharp
public static string FormatRestoreResult(DisplayRestoreResult result) =>
    result.Success
        ? "restore-physical: success verified=True"
        : $"restore-physical: failed: {result.Error}";
```

- [x] **Step 4: Route probe restore through backend**

In `Program.cs`, replace the raw `api.RestorePhysicalPrimaryAsync` call in the `RestorePhysicalDisplayProbeCommand` case with:

```csharp
var backend = new WindowsDisplayBackend(api);
DisplayRestoreResult restoreResult = await backend.RestorePhysicalPrimaryAsync(CancellationToken.None);
Console.WriteLine(DisplayProbeFormatter.FormatRestoreResult(restoreResult));
return restoreResult.Success ? 0 : 2;
```

- [x] **Step 5: Verify green**

Run:

```powershell
dotnet test tests/Beacon.DisplayProbe.Tests/Beacon.DisplayProbe.Tests.csproj --filter FormatRestoreResult
```

Expected: formatter tests pass.

## Task 4: Docs And Validation

**Files:**
- Modify: `docs/windows-display-backend.md`
- Modify: `README.md`
- Modify: this plan file

- [x] **Step 1: Update docs**

In `docs/windows-display-backend.md`, add to `Probe Commands`:

```markdown
`restore-physical` uses the verified backend path, not the raw one-shot API call. The command can fail even after Windows accepts the DisplayConfig apply if the follow-up topology query still shows a virtual primary or no physical primary.
```

In `README.md`, add to the display lifecycle summary:

```markdown
Physical restore is verified: Beacon queries topology after restore and treats unverified physical-primary state as a recovery failure instead of silently continuing.
```

- [x] **Step 2: Static validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
```

Expected: both commands pass.

- [x] **Step 3: Dynamic validation**

Run:

```powershell
dotnet test Beacon.slnx
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright lint
pnpm --dir tests\Beacon.ClientLab.Playwright test
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: all commands pass; existing Gradle deprecation warnings are acceptable if the command exits successfully.

- [x] **Step 4: Boundary audit**

Run:

```powershell
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
```

Expected: no matches introduced by this milestone.

- [ ] **Step 5: Sync**

Run:

```powershell
git add src/Beacon.Platform.Windows/Displays/WindowsDisplayApi.cs src/Beacon.Platform.Windows/Displays/WindowsDisplayBackend.cs src/Beacon.DisplayProbe/DisplayProbeFormatter.cs src/Beacon.DisplayProbe/Program.cs tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayBackendTests.cs tests/Beacon.Platform.Windows.Tests/Displays/DisplayTopologySnapshotTests.cs tests/Beacon.DisplayProbe.Tests/DisplayProbeFormatterTests.cs docs/windows-display-backend.md README.md docs/superpowers/plans/2026-07-08-beacon-stream-milestone-21-physical-restore-verification.md
git commit -m "Verify physical display restore"
git push -u origin codex/milestone-21-physical-restore-verification
gh pr create --draft --base main --head codex/milestone-21-physical-restore-verification --title "Verify physical display restore" --body "Milestone 21 verifies physical-primary restore and routes the display probe through the verified backend path."
```

Expected: branch pushed and draft PR created for CI.

## Task 5: Refactor, Revalidate, And Merge

**Files:**
- Modify only files touched by Tasks 1-4 if cleanup is needed.

- [ ] **Step 1: Refactor after green**

Remove duplicate restore formatting or diagnostic wording only if the validation output shows unnecessary duplication. Do not change lifecycle behavior.

- [ ] **Step 2: Re-run targeted validation**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter "WindowsDisplayBackendTests|DisplayTopologySnapshotTests"
dotnet test tests/Beacon.DisplayProbe.Tests/Beacon.DisplayProbe.Tests.csproj
```

Expected: all targeted tests pass.

- [ ] **Step 3: Watch CI and merge**

Run:

```powershell
gh pr checks --watch
gh pr ready
gh pr merge --merge --delete-branch
git switch main
git pull --ff-only
```

Expected: PR checks pass, PR merges, and local `main` is clean.

## Non-Goals

- No timeout-based wait for Windows topology convergence.
- No background watchdog.
- No physical-display fallback for unavailable virtual displays.
- No SudoVDA driver rebuild or HDR capability change.
- No changes to session ownership cleanup rules.
