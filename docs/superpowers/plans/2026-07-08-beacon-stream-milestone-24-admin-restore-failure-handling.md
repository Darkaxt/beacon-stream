# Admin Restore Failure Handling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the admin physical-display restore endpoint report backend restore failures as HTTP 503 instead of returning success.

**Architecture:** `IDisplayBackend.RestorePhysicalPrimaryAsync` already returns a verified `DisplayRestoreResult`. The admin endpoint should preserve that state at the HTTP boundary while still publishing the existing diagnostic event.

**Tech Stack:** ASP.NET Core minimal APIs, xUnit, `WebApplicationFactory`, Beacon fake display backend.

---

## File Map

- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs`
  - Adds the regression test for failed admin physical restore.
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`
  - Changes `/admin/recovery/restore-physical` to return `503 ServiceUnavailable` on failed restore verification.
- Update: `README.md`
  - Clarifies that admin restore reports verified failure rather than acknowledging success.

## Root Cause

`/admin/recovery/restore-physical` already receives a `DisplayRestoreResult` and emits an error diagnostic when `Success` is false. The endpoint then unconditionally returns `200 OK` with `{ restoreRequested = true }`, discarding the verified failure and hiding the actionable reason from Cockpit, scripts, and future APK control surfaces.

## Task 1: Reproduce the HTTP Boundary Bug

**Files:**
- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs`

- [x] **Step 1: Write the failing test**

Add `AdminPhysicalRestoreReturnsServiceUnavailableWhenRestoreFails` using `FakeDisplayBackend.NextRestoreResult = DisplayRestoreResult.Fail("physical primary was not verified")`.

- [x] **Step 2: Run the focused test red**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter AdminPhysicalRestoreReturnsServiceUnavailableWhenRestoreFails
```

Expected before the fix: FAIL because the endpoint returns `OK` instead of `ServiceUnavailable`.

## Task 2: Preserve Restore Failure at the Admin API Boundary

**Files:**
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`

- [x] **Step 1: Return OK only on success**

After publishing the diagnostic event, return:

```csharp
return result.Success
    ? Results.Ok(new { restoreRequested = true })
    : Results.Problem(result.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
```

- [x] **Step 2: Run focused green verification**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "AdminPhysicalRestoreReturnsServiceUnavailableWhenRestoreFails|AdminCanRequestPhysicalRestoreAndClientRecovery|SnapshotIncludesRecentOperationalDiagnostics"
```

Expected: all selected tests pass.

## Task 3: Documentation and Full Validation

**Files:**
- Modify: `README.md`

- [x] **Step 1: Update recovery documentation**

Clarify that admin physical restore returns `503` with the verified backend error when the physical primary cannot be confirmed.

- [x] **Step 2: Run full static and dynamic validation**

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

Expected: all commands pass; the final `rg` returns no matches.

- [ ] **Step 3: Sync**

Commit, push, open a PR, wait for CI, mark ready, merge, and delete the branch when green.
