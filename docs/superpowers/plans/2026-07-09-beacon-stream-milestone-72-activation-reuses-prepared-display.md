# Activation Reuses Prepared Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make launch activation reuse a prepared per-client virtual display instead of creating that display again.

**Architecture:** `WindowsDisplayBackend.EnsureVirtualDisplayAsync` should first inspect the current topology. If the requested per-client display already exposes the requested mode, activation skips `CreateVirtualDisplayAsync`, verifies extended topology, and applies virtual-primary. `WindowsDisplayApi` persists the logical client display id to Windows display name mapping so a prepared lease can be recognized across `DisplayProbe` processes or server restarts. Cleanup remains scoped: only a display created during the same activation attempt is removed on activation failure.

**Tech Stack:** .NET 10/C#, `Beacon.Platform.Windows` fakeable display API tests.

---

## Requirements

- `REQ-DISP-002`: one leased virtual display belongs to the client lifecycle.
- `REQ-DISP-003`: the leased display can be created or verified before app launch.
- `REQ-DISP-004`: launch binds to the client's prepared display.
- `REQ-DISP-010`: mirror mode is prohibited by default.
- `REQ-DISP-011`: Beacon must not fall back to the physical display when the virtual display is unavailable.
- `REQ-DISP-018`: display decisions are logged with before/after topology, display id, resolution, refresh, primary flag, HDR flag, and reason.
- `REQ-TEST-001`: validation must not require the phone.
- `REQ-TEST-009`: real Windows/SudoVDA checks stay manually runnable and separate from fast tests.

## File Map

- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayBackend.cs`
  - Reuse an already prepared display during activation.
  - Only remove a virtual display after activation failures when activation created it in that same call.
- Create: `src/Beacon.Platform.Windows/Displays/WindowsDisplayNameMap.cs`
  - Persist and reload logical display id to Windows display name mappings.
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayApi.cs`
  - Load persisted display-name mappings on startup.
  - Remember mappings after successful create and forget them after remove.
  - Apply logical mapping only to virtual display paths.
- Modify: `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayBackendTests.cs`
  - Add a red test proving prepared displays activate without another create call.
- Create: `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayNameMapTests.cs`
  - Add persistence and forget coverage for the durable map.

## Tasks

### Task 1: Red test

- [ ] Add `EnsureVirtualDisplayAsync_WhenDisplayAlreadyPrepared_ActivatesWithoutCreatingAgain`.
- [ ] Arrange current topology with physical primary and the target virtual display already extended at `2560x1600@120`.
- [ ] Run:

```powershell
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter EnsureVirtualDisplayAsync_WhenDisplayAlreadyPrepared_ActivatesWithoutCreatingAgain
```

Expected: fail because activation still calls `CreateVirtualDisplayAsync`.

### Task 2: Implementation

- [ ] In `EnsureVirtualDisplayAsync`, query topology before create.
- [ ] If the existing topology already has the target display at the requested mode, skip create and use that topology for mirror/mode verification.
- [ ] Track whether activation created the display.
- [ ] On mirror, mode, primary apply, or primary verification failure, remove only displays created during this activation call.
- [ ] Run the focused test again.

Expected: pass.

### Task 3: Regression validation

- [ ] Run:

```powershell
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter WindowsDisplayBackendTests
```

Expected: pass.

### Task 4: Cross-process mapping validation

- [ ] Add `WindowsDisplayNameMapTests`.
- [ ] Run:

```powershell
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter WindowsDisplayNameMapTests
```

Expected: pass.

- [ ] Run a real Windows smoke:

```powershell
dotnet run --project src\Beacon.DisplayProbe -- prepare --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src\Beacon.DisplayProbe -- ensure --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src\Beacon.DisplayProbe -- remove --client z-fold-7
dotnet run --project src\Beacon.DisplayProbe -- status
```

Expected: prepare and ensure both succeed in separate processes, and final status returns physical-primary.

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
gradle --no-daemon -p src\Beacon.Android test assembleDebug
dotnet run --project src\Beacon.DisplayProbe -- status
```

Expected: all commands pass; the timeout audit returns no matches.

- [ ] Commit with message `Activate prepared display without recreating`.
- [ ] Push `codex/milestone-72-activation-reuses-prepared-display`.
- [ ] Open a PR with validation evidence.
- [ ] Watch CI and merge when green.
