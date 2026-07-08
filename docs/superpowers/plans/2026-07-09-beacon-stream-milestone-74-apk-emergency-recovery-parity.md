# APK Emergency Recovery Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the owning APK emergency restore endpoint use the same per-client display recovery path as local admin recovery.

**Architecture:** Keep the profile permission gate on `/clients/{clientId}/emergency-restore`. After the gate passes, compute that client's leased display id and call `DisplayLeaseManager.RecoverDisplayAsync` instead of one-shot `IDisplayBackend.RestorePhysicalPrimaryAsync`. This lets the owning APK recover from stale physical-restore topology by removing its own virtual display lease, while still preventing arbitrary client access to admin-only broader recovery routes.

**Tech Stack:** .NET 10/C#, ASP.NET minimal APIs, fake display backend server tests.

---

## Requirements

- `REQ-DISP-009`: manual recovery can override lifecycle rules when explicitly requested by the owning APK emergency action.
- `REQ-MODE-004`: if blackout or virtual-primary recovery is needed, the owning APK and WPF cockpit must both be able to request emergency restore.
- `REQ-REC-005`: the APK may call emergency actions for its own active session.
- `REQ-REC-007`: recovery remains an escape hatch, not normal lifecycle cleanup.
- `REQ-TEST-001`: validation must not require the phone.

## File Map

- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
  - Change `/clients/{clientId}/emergency-restore` from raw display restore to `DisplayLeaseManager.RecoverDisplayAsync`.
  - Return the same client-scoped response shape when recovery succeeds.
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
  - Add a regression test where initial restore fails, virtual display removal succeeds, final health verifies physical primary, and the client emergency endpoint returns success.
  - Update the existing restore-failure test to expect the stronger recovery sequence and fail only when recovery cannot remove/verify.
- Modify: `README.md`
  - Document that client emergency restore uses client-scoped display recovery, while admin recovery remains broader.
- Modify: `src/Beacon.ClientLab/src/main.ts`
  - Keep the simulator's emergency restore response contract aligned with the server response.
- Modify: `tests/Beacon.ClientLab.Playwright/tests/client-lab.spec.ts`
  - Validate the simulator displays client-scoped recovery success.

## Tasks

### Task 1: Red server endpoint tests

- [x] Add `EmergencyRestoreUsesClientScopedRecoveryAfterInitialRestoreFailure`.
- [x] Arrange `FakeDisplayBackend.RestoreResults` so the first restore fails and the second restore succeeds, then call `/clients/z-fold-7/emergency-restore`.
- [x] Assert HTTP 200, `clientId=z-fold-7`, `displayId=client-z-fold-7`, `recovered=true`, two restore calls, and one remove call.
- [x] Update `EmergencyRestoreReturnsServiceUnavailableWhenPhysicalRestoreFails` so it models unrecoverable failure after remove, not just initial restore failure.
- [x] Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "EmergencyRestoreUsesClientScopedRecoveryAfterInitialRestoreFailure|EmergencyRestoreReturnsServiceUnavailableWhenPhysicalRestoreFails"
```

Expected: new test fails because the endpoint does not call `RecoverDisplayAsync`; updated failure test may fail because no remove is attempted.

### Task 2: Endpoint implementation

- [x] Inject `DisplayLeaseManager leases` instead of `IDisplayBackend displayBackend` into the emergency endpoint.
- [x] Compute `DisplayLease.CreateDisplayId(new ClientId(clientId))`.
- [x] Call `leases.RecoverDisplayAsync(displayId, cancellationToken)`.
- [x] Return `200 OK` with `{ clientId, displayId, recovered = true }` when successful.
- [x] Return `503` with the recovery error when unsuccessful.
- [x] Run the focused tests again.

Expected: pass.

### Task 3: Docs and regression validation

- [x] Update README recovery text to say owning-client emergency restore uses client-scoped recovery and can remove that client's virtual display lease after restore verification fails.
- [x] Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter ClientApiTests
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
```

Expected: pass.

### Task 4: Full validation and sync

- [x] Run:

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

- [ ] Commit with message `Use client-scoped emergency display recovery`.
- [ ] Push `codex/milestone-74-apk-emergency-recovery-parity`.
- [ ] Open a PR with validation evidence.
- [ ] Watch CI and merge when green.
