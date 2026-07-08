# Display Preflight Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Attempt one safe display repair during lease preflight before failing when the requested virtual display is unavailable.

**Architecture:** Keep repair policy in `DisplayLeaseManager`, because it already owns display preflight and cleanup decisions. On first virtual-display ensure failure, restore the physical primary display, then retry the same requested virtual display once. Every branch publishes diagnostics through the existing optional sink; no timeout, background watchdog, or repeated retry loop is introduced.

**Tech Stack:** .NET 10, existing `IDisplayBackend`, fake display backend, diagnostics event journal, xUnit.

---

## Requirements Covered

- `REQ-DISP-011`: no silent fallback to the physical display.
- `REQ-DISP-012`: attempt safe preflight repair before failing when the virtual display is unavailable.
- `REQ-DISP-013`: failed repair remains explicit and diagnostic.
- `REQ-DISP-014`: physical-primary restore is requested as part of recovery/repair.
- `REQ-REC-008`: repair attempts and failures are visible in diagnostics.
- `REQ-TEST-007`: behavior is covered with fake display backends, no phone required.

## File Map

- Modify: `src/Beacon.Core/Displays/FakeDisplayBackend.cs`
  - Add queued ensure results for deterministic first-fail/second-pass tests.
- Modify: `src/Beacon.Core/Displays/DisplayLeaseManager.cs`
  - Add one safe repair attempt after initial ensure failure.
  - Publish `lease.ensure.repair` diagnostics.
- Modify: `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs`
  - Add repair success, restore failure, and second ensure failure coverage.
- Modify: `README.md`
  - Document display preflight repair behavior.
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-20-display-preflight-repair.md`
  - Track execution state.

## Task 1: Fake-Backed Display Repair Tests

**Files:**
- Modify: `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs`
- Modify: `src/Beacon.Core/Displays/FakeDisplayBackend.cs`

- [ ] **Step 1: Write failing repair tests**

Add to `DisplayLeaseManagerTests`:

```csharp
[Fact]
public async Task EnsureLeaseRepairsOnceAfterInitialVirtualDisplayFailure()
{
    var backend = new FakeDisplayBackend();
    backend.EnsureResults.Enqueue(DisplayEnsureResult.Fail("virtual display disappeared"));
    backend.EnsureResults.Enqueue(DisplayEnsureResult.Ok());
    var sink = new RecordingDiagnosticSink();
    var manager = new DisplayLeaseManager(backend, sink);

    DisplayLeaseResult result = await manager.EnsureLeaseAsync(ClientProfile.CreateZFold7Default(), CancellationToken.None);

    Assert.True(result.Success, result.Error);
    Assert.NotNull(result.Lease);
    Assert.Equal(2, backend.EnsureCalls.Count);
    Assert.Equal("physical-primary", Assert.Single(backend.RestoreCalls));
    Assert.Contains(sink.Events, evt => evt.Operation == "lease.ensure.repair" && evt.Severity == "information");
}

[Fact]
public async Task EnsureLeaseFailsWhenRepairRestoreFails()
{
    var backend = new FakeDisplayBackend
    {
        NextRestoreResult = DisplayRestoreResult.Fail("physical primary could not be restored")
    };
    backend.EnsureResults.Enqueue(DisplayEnsureResult.Fail("virtual display disappeared"));
    var sink = new RecordingDiagnosticSink();
    var manager = new DisplayLeaseManager(backend, sink);

    DisplayLeaseResult result = await manager.EnsureLeaseAsync(ClientProfile.CreateZFold7Default(), CancellationToken.None);

    Assert.False(result.Success);
    Assert.Null(result.Lease);
    Assert.Single(backend.EnsureCalls);
    Assert.Equal("physical-primary", Assert.Single(backend.RestoreCalls));
    Assert.Contains(result.Error, "repair failed", StringComparison.OrdinalIgnoreCase);
    Assert.Contains(sink.Events, evt => evt.Operation == "lease.ensure.repair" && evt.Severity == "error");
}

[Fact]
public async Task EnsureLeaseFailsExplicitlyWhenSecondEnsureFailsAfterRepair()
{
    var backend = new FakeDisplayBackend();
    backend.EnsureResults.Enqueue(DisplayEnsureResult.Fail("virtual display disappeared"));
    backend.EnsureResults.Enqueue(DisplayEnsureResult.Fail("driver still unavailable"));
    var sink = new RecordingDiagnosticSink();
    var manager = new DisplayLeaseManager(backend, sink);

    DisplayLeaseResult result = await manager.EnsureLeaseAsync(ClientProfile.CreateZFold7Default(), CancellationToken.None);

    Assert.False(result.Success);
    Assert.Null(result.Lease);
    Assert.Equal(2, backend.EnsureCalls.Count);
    Assert.Equal("physical-primary", Assert.Single(backend.RestoreCalls));
    Assert.Contains("after repair", result.Error, StringComparison.OrdinalIgnoreCase);
    Assert.Contains(sink.Events, evt => evt.Operation == "lease.ensure.repair" && evt.Severity == "error");
}
```

- [ ] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
```

Expected: compile failure for missing `FakeDisplayBackend.EnsureResults`, or test failure because repair is not implemented.

- [ ] **Step 3: Add queued fake ensure results**

In `FakeDisplayBackend`, add:

```csharp
public Queue<DisplayEnsureResult> EnsureResults { get; } = [];
```

Change `EnsureVirtualDisplayAsync` to:

```csharp
if (EnsureResults.Count > 0)
{
    return Task.FromResult(EnsureResults.Dequeue());
}

return Task.FromResult(AllowEnsure
    ? DisplayEnsureResult.Ok()
    : DisplayEnsureResult.Fail("virtual display is unavailable"));
```

- [ ] **Step 4: Implement one safe repair attempt**

In `DisplayLeaseManager.EnsureLeaseAsync`, after the first ensure failure:

1. Publish the existing `lease.ensure` error for the initial failure.
2. Call `RestorePhysicalPrimaryAsync`.
3. If restore fails, publish `lease.ensure.repair` error and return:

```csharp
return new DisplayLeaseResult(
    false,
    null,
    $"{ensureResult.Error}; repair failed because physical primary restore failed: {restoreResult.Error}; refusing to fall back to physical display.");
```

4. Publish `lease.ensure.repair` information for the restore request.
5. Retry `EnsureVirtualDisplayAsync` once with the same display id, width, height, refresh, and HDR preference.
6. If second ensure fails, publish `lease.ensure.repair` error and return:

```csharp
return new DisplayLeaseResult(
    false,
    null,
    $"{ensureResult.Error}; after repair, virtual display ensure still failed: {repairEnsure.Error}; refusing to fall back to physical display.");
```

7. Continue creating the lease when the second ensure succeeds.

- [ ] **Step 5: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
```

Expected: all display lease manager tests pass.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/Beacon.Core/Displays/FakeDisplayBackend.cs src/Beacon.Core/Displays/DisplayLeaseManager.cs tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs docs/superpowers/plans/2026-07-08-beacon-stream-milestone-20-display-preflight-repair.md
git commit -m "Add display preflight repair"
git push -u origin codex/milestone-20-display-preflight-repair
```

Expected: branch is pushed with the repair checkpoint.

## Task 2: Docs, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-20-display-preflight-repair.md`

- [ ] **Step 1: Update docs**

Add to `README.md` under Display lifecycle checks:

```markdown
Display preflight attempts one safe repair before failing: if the first virtual-display ensure fails, Beacon restores the physical primary display and retries the same requested virtual display once. If repair fails, launch still stops before app/stream side effects and the diagnostic journal records the reason.
```

- [ ] **Step 2: Static validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
```

Expected: both commands pass.

- [ ] **Step 3: Dynamic validation**

Run:

```powershell
dotnet test Beacon.slnx
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright lint
pnpm --dir tests\Beacon.ClientLab.Playwright test
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: all commands pass; the Gradle 9 deprecation warning remains acceptable if the command exits successfully.

- [ ] **Step 4: Boundary audit**

Run:

```powershell
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
```

Expected: no matches; repair must not introduce timeout-based lifecycle behavior.

- [ ] **Step 5: Commit and sync**

Run:

```powershell
git add README.md docs/superpowers/plans/2026-07-08-beacon-stream-milestone-20-display-preflight-repair.md
git commit -m "Document display preflight repair"
git push
gh pr create --draft --base main --head codex/milestone-20-display-preflight-repair --title "Add display preflight repair" --body "Milestone 20 display preflight repair implementation."
gh pr checks 20 --watch
gh pr ready 20
gh pr merge 20 --merge --delete-branch
```

Expected: PR checks pass, PR merges, and local `main` is clean.

## Non-Goals

- No repeated retry loop.
- No timeout-based waiting.
- No background watchdog.
- No physical-display fallback when the virtual display remains unavailable.
- No real Windows driver changes.
