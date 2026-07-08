# Prepared Display Lease Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split Beacon's display lifecycle so active client beacon creates or verifies the per-client virtual display without making it primary, while launch still activates the virtual display as primary for the session.

**Architecture:** Add an explicit prepare operation to the display backend and lease manager. Active beacon calls prepare; launch and reconnect keep using the existing activation path. The Windows backend creates/verifies the virtual display mode during prepare, rejects mirror mode, restores physical primary if creation made the virtual display primary, and logs the before/after topology decision.

**Tech Stack:** .NET 10/C#, ASP.NET minimal APIs, fake display backend tests, Windows display backend abstraction tests, Client Lab no-phone flow.

---

## Requirements

- `REQ-CTRL-014`: active beacon prepares the client display lease before launch without moving display policy into the client.
- `REQ-DISP-002`: a client can have one leased virtual display prepared for that client's lifecycle.
- `REQ-DISP-003`: the leased display is created or verified during preflight before app launch.
- `REQ-DISP-010`: mirror mode is prohibited by default.
- `REQ-DISP-011`: Beacon must not fall back to the physical display when the virtual display is unavailable.
- `REQ-DISP-014`: physical primary restore is verified.
- `REQ-DISP-016`: Beacon must prevent the laptop panel from being stranded inactive when no session owns that state.
- `REQ-DISP-017`: Beacon must support keeping the virtual display present while owned work still runs there, without stealing the laptop main display.
- `REQ-DISP-018`: display decisions are logged with before/after topology, display id, resolution, refresh, primary flag, HDR flag, and reason.
- `REQ-TEST-001`: validation must not require the phone.
- `REQ-TEST-009`: real Windows/SudoVDA checks stay manually runnable and separate from fast tests.

## File Map

- Modify: `src/Beacon.Core/Displays/IDisplayBackend.cs`
  - Add `PrepareVirtualDisplayAsync(...)`.
- Modify: `src/Beacon.Core/Displays/DisplayLeaseManager.cs`
  - Add `PrepareLeaseAsync(...)`.
  - Keep `EnsureLeaseAsync(...)` as the session activation path.
- Modify: `src/Beacon.Core/Displays/FakeDisplayBackend.cs`
  - Record prepare calls independently from activation calls.
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayBackend.cs`
  - Implement prepare without `SetVirtualPrimaryAsync`.
  - Restore physical primary if the new display creation leaves the virtual display primary.
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
  - Change active beacon to call `PrepareLeaseAsync`.
  - Keep launch and reconnect on `EnsureLeaseAsync`.
- Modify: `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs`
  - Add tests proving prepare is not activation.
- Modify: `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayBackendTests.cs`
  - Add tests for prepare creating/verifying extended topology and restoring physical primary if creation steals it.
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
  - Add/adjust active beacon tests to assert prepare, not activation.
- Modify: `README.md`
  - Record Milestone 70 behavior and clarify active beacon versus launch activation.
- Modify: `docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md`
  - Clarify that active beacon prepares a display lease without making virtual-primary until launch.

## Tasks

### Task 1: Red tests for prepare versus activate

- [ ] Add a `DisplayLeaseManagerTests` test named `PrepareLeaseCreatesClientScopedDisplayWithoutActivatingPrimary`.
- [ ] Add a `ClientApiTests` test or update `BeaconActiveClientEnsuresDisplayLeaseBeforeLaunch` so active beacon expects one prepare call and zero activation calls.
- [ ] Add a `WindowsDisplayBackendTests` test named `PrepareVirtualDisplayAsync_CreatesDisplayWithoutPrimaryRequest`.
- [ ] Run:

```powershell
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter BeaconActiveClientEnsuresDisplayLeaseBeforeLaunch
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter PrepareVirtualDisplayAsync_CreatesDisplayWithoutPrimaryRequest
```

Expected: fail because the prepare API does not exist or active beacon still uses activation.

### Task 2: Add display prepare contract

- [ ] Add `PrepareVirtualDisplayAsync(...)` to `IDisplayBackend` with the same arguments and result type as `EnsureVirtualDisplayAsync(...)`.
- [ ] Add `PrepareLeaseAsync(...)` to `DisplayLeaseManager`.
- [ ] Add `PrepareCalls` recording to `FakeDisplayBackend`.
- [ ] Change active beacon to call `PrepareLeaseAsync`.
- [ ] Run the Task 1 tests again.

Expected: core/server tests pass; Windows prepare test still fails until backend implementation is added.

### Task 3: Implement Windows prepare topology behavior

- [ ] Implement `WindowsDisplayBackend.PrepareVirtualDisplayAsync(...)`.
- [ ] Reject driver-not-ready, create failure, mirror mode, and exact-mode mismatch with diagnostics.
- [ ] Do not call `SetVirtualPrimaryAsync(...)`.
- [ ] Query HDR capability truthfully using the existing HDR negotiation.
- [ ] If the post-create topology has the virtual display primary or physical primary is not verified, call `RestorePhysicalPrimaryAsync(...)` and verify it succeeds.
- [ ] Add a second Windows backend test named `PrepareVirtualDisplayAsync_WhenCreateStealsPrimary_RestoresPhysicalPrimaryAndKeepsDisplay`.
- [ ] Run:

```powershell
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter WindowsDisplayBackendTests
```

Expected: pass.

### Task 4: Docs and no-phone validation

- [ ] Update README milestone summary and Client Lab/Display sections.
- [ ] Update the requirement register text for `REQ-CTRL-014` to state active beacon prepares without virtual-primary activation.
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

Expected: all commands pass; `rg` should return no matches.

### Task 5: Sync

- [ ] Commit all milestone files with message `Split prepared display lease from activation`.
- [ ] Push `codex/milestone-70-prepare-display-lease`.
- [ ] Open a PR with validation evidence.
- [ ] Watch CI and merge when green.
