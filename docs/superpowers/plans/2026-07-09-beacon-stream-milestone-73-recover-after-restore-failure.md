# Recover After Restore Failure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make manual per-client display recovery restore control even when the first physical-primary restore verification fails in a virtual-primary topology.

**Architecture:** Keep verified restore in `WindowsDisplayBackend`, where topology reconciliation already exists. `WindowsDisplayApi.RestorePhysicalPrimaryAsync` reports DisplayConfig apply success/failure only; the backend verifies physical primary and retries until success or repeated topology. Change manual lease recovery (`DisplayLeaseManager.RecoverDisplayAsync`) so it removes the client virtual display after an initial restore failure, then verifies physical primary again, with a final health check for stale post-remove topology. Automatic cleanup remains conservative and does not remove a display just because restore failed.

**Tech Stack:** .NET 10/C#, core display lease tests, fake display backend.

---

## Requirements

- `REQ-DISP-014`: physical primary restore is verified.
- `REQ-DISP-016`: Beacon prevents the laptop panel from being stranded inactive when no session owns that state.
- `REQ-DISP-017`: the virtual display can remain alive for owned work, but manual recovery can explicitly terminate that lease.
- `REQ-CTRL-011`: manual web/admin control can terminate sessions and restore physical control.
- `REQ-TEST-001`: validation must not require the phone.

## File Map

- Modify: `src/Beacon.Core/Displays/DisplayLeaseManager.cs`
  - In manual recovery only, remove the virtual display even when initial physical restore fails.
  - Verify physical primary again after removal.
  - If the final restore result is stale but display health verifies physical primary, return success.
  - Preserve failure diagnostics if removal or final verification fails.
- Modify: `src/Beacon.Core/Displays/FakeDisplayBackend.cs`
  - Add queued restore results for tests that need first-restore-fails, second-restore-succeeds behavior.
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayApi.cs`
  - Stop doing immediate physical-primary verification in the low-level apply method; `WindowsDisplayBackend` owns verified topology reconciliation.
- Modify: `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs`
  - Replace the old "restore failure does not remove" expectation with manual recovery fallback behavior.
- Modify: `src/Beacon.DisplayProbe/DisplayProbeCommandLine.cs`
  - Add `recover --client <id>`.
- Modify: `src/Beacon.DisplayProbe/DisplayProbeApp.cs`
  - Dispatch `recover` through `DisplayLeaseManager.RecoverDisplayAsync`.
- Modify: `src/Beacon.DisplayProbe/DisplayProbeFormatter.cs`
  - Add recovery result formatting.
- Modify: `tests/Beacon.DisplayProbe.Tests/*`
  - Cover command parsing, formatting, and the no-phone recovery sequence.

## Tasks

### Task 1: Red test

- [ ] Change `RecoverDisplayAsyncWhenRestoreFailsDoesNotRemoveVirtualDisplay` into `RecoverDisplayAsyncWhenInitialRestoreFailsRemovesVirtualDisplayAndVerifiesAfterRemove`.
- [ ] Arrange first restore failure, remove success, second restore success.
- [ ] Add a second regression where final restore still reports stale topology but display health verifies physical primary.
- [ ] Run:

```powershell
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter RecoverDisplayAsyncWhenInitialRestoreFailsRemovesVirtualDisplayAndVerifiesAfterRemove
```

Expected: fail because recovery currently returns after the initial restore failure.

### Task 2: Implementation

- [ ] Add queued restore results to `FakeDisplayBackend`.
- [ ] Update `DisplayLeaseManager.RecoverDisplayAsync`.
- [ ] Update `WindowsDisplayApi.RestorePhysicalPrimaryAsync` so the backend can reconcile stale topology after apply.
- [ ] Run the focused test again.

Expected: pass.

### Task 3: Regression validation

- [ ] Run:

```powershell
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter AdminCanRemoveSelectedClientDisplayLease
```

Expected: pass.

### Task 4: DisplayProbe recovery command

- [ ] Add parser, formatter, and app tests for `recover --client z-fold-7`.
- [ ] Implement the command.
- [ ] Run:

```powershell
dotnet test tests\Beacon.DisplayProbe.Tests\Beacon.DisplayProbe.Tests.csproj --filter "ParseRecoverCommandRequiresClient|FormatRecoveryResultReportsSuccess|RecoverCommandRemovesDisplayAfterInitialRestoreFailure"
```

Expected: pass.

- [ ] Run a real Windows smoke:

```powershell
dotnet run --project src\Beacon.DisplayProbe -- prepare --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src\Beacon.DisplayProbe -- ensure --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src\Beacon.DisplayProbe -- recover --client z-fold-7
dotnet run --project src\Beacon.DisplayProbe -- status
```

Expected: recover succeeds and final status reports physical primary.

### Task 5: Full validation and sync

- [ ] Run:

```powershell
git diff --check
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
dotnet test Beacon.slnx --no-build
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir src\Beacon.ClientLab build
pnpm --dir tests\Beacon.ClientLab.Playwright test
C:\Users\darka\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat --no-daemon -p src\Beacon.Android test assembleDebug
dotnet run --project src\Beacon.DisplayProbe -- status
```

- [ ] Commit with message `Recover display after restore failure`.
- [ ] Push `codex/milestone-73-recover-after-restore-failure`.
- [ ] Open a PR with validation evidence.
- [ ] Watch CI and merge when green.
