# Display Probe Prepare Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a manual no-phone DisplayProbe command that exercises the prepared display lease path without activating virtual-primary.

**Architecture:** DisplayProbe gets a `prepare` command parallel to `ensure`. `prepare` parses the same client, mode, refresh, and HDR arguments, calls `WindowsDisplayBackend.PrepareVirtualDisplayAsync`, and formats output as a prepare result. Existing `ensure` remains the activation path that makes the virtual display primary.

**Tech Stack:** .NET 10/C#, `Beacon.DisplayProbe`, `Beacon.Platform.Windows` fakeable display API tests.

---

## Requirements

- `REQ-CTRL-014`: active beacon prepares the display lease before launch.
- `REQ-DISP-002`: a client can have one leased virtual display prepared for that client's lifecycle.
- `REQ-DISP-003`: the leased display can be created or verified before app launch.
- `REQ-DISP-014`: physical primary restore is verified by the backend.
- `REQ-DISP-016`: Beacon prevents the laptop panel from being stranded inactive when no session owns that state.
- `REQ-DISP-017`: Beacon supports keeping the virtual display present without stealing the laptop main display.
- `REQ-TEST-001`: validation does not require the phone.
- `REQ-TEST-009`: real Windows/SudoVDA integration checks stay manually runnable.

## File Map

- Modify: `src/Beacon.DisplayProbe/DisplayProbeCommandLine.cs`
  - Add `PrepareDisplayProbeCommand`.
  - Parse `prepare --client ... --width ... --height ... --refresh ... --hdr ...`.
- Modify: `src/Beacon.DisplayProbe/DisplayProbeApp.cs`
  - Dispatch `PrepareDisplayProbeCommand` to `WindowsDisplayBackend.PrepareVirtualDisplayAsync`.
- Modify: `src/Beacon.DisplayProbe/DisplayProbeFormatter.cs`
  - Add `FormatPrepareResult`.
- Modify: `tests/Beacon.DisplayProbe.Tests/DisplayProbeCommandLineTests.cs`
  - Add parse coverage for `prepare`.
- Modify: `tests/Beacon.DisplayProbe.Tests/DisplayProbeAppTests.cs`
  - Add app-level coverage proving prepare creates without primary request.
- Modify: `tests/Beacon.DisplayProbe.Tests/DisplayProbeFormatterTests.cs`
  - Add prepare result formatting coverage.
- Modify: `README.md`
  - Document the manual prepare command beside status/ensure/restore.

## Tasks

### Task 1: Red tests

- [ ] Add `ParsePrepareCommandPreservesSixteenByTenAndRefresh`.
- [ ] Add `FormatPrepareResultReportsHdrReason`.
- [ ] Add `PrepareCommandCreatesDisplayWithoutPrimaryRequest`.
- [ ] Run:

```powershell
dotnet test tests\Beacon.DisplayProbe.Tests\Beacon.DisplayProbe.Tests.csproj --filter "ParsePrepareCommandPreservesSixteenByTenAndRefresh|FormatPrepareResultReportsHdrReason|PrepareCommandCreatesDisplayWithoutPrimaryRequest"
```

Expected: fail because the command, formatter, or app dispatch does not exist.

### Task 2: Implementation

- [ ] Add `PrepareDisplayProbeCommand`.
- [ ] Add shared parsing for prepare/ensure command arguments.
- [ ] Add `DisplayProbeFormatter.FormatPrepareResult`.
- [ ] Add `DisplayProbeApp` prepare dispatch.
- [ ] Extend the probe test fake only enough to record create/primary calls and update topology after create.
- [ ] Run the focused DisplayProbe tests again.

Expected: pass.

### Task 3: Docs

- [ ] Add the prepare command to the README Display lifecycle checks block:

```powershell
dotnet run --project src/Beacon.DisplayProbe -- prepare --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
```

- [ ] Add one sentence explaining prepare verifies the display as extended/non-primary and ensure activates virtual-primary.

### Task 4: Validation and sync

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

- [ ] Commit with message `Add DisplayProbe prepare command`.
- [ ] Push `codex/milestone-71-display-probe-prepare`.
- [ ] Open a PR with validation evidence.
- [ ] Watch CI and merge when green.
