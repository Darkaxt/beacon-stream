# Stream Stop Failure Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make client and admin stream-stop endpoints report streaming backend stop failures as HTTP 503 instead of collapsing them into 404.

**Architecture:** A missing session plan remains `404`. Once a plan exists, `IStreamingBackend.StopAsync` failure is a backend/service failure and should surface as `503 ServiceUnavailable` with the original stop error. Both client and admin stop routes share the same boundary rule.

**Tech Stack:** ASP.NET Core minimal APIs, xUnit, `WebApplicationFactory`, local fake `IStreamingBackend` in server API tests.

---

## File Map

- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
  - Adds client-route regression coverage for streaming stop failure.
- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs`
  - Adds admin-route regression coverage for streaming stop failure.
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
  - Returns `503` for `StopAsync` failures when a session plan exists.
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`
  - Applies the same rule to the admin selected-client stream stop route.
- Update: `README.md`
  - Documents that stop failures surface as backend failures rather than missing-session errors.

## Root Cause

Both stream-stop endpoints already distinguish a missing session plan before calling the streaming backend. After that, they call `StopAsync`, but map any unsuccessful result to `404 NotFound`. This hides a real external wrapper/process stop failure behind a missing-session response, even though `ExternalProcessStreamingBackend` already models stop failure as `StreamingStopResult.Fail`.

## Task 1: Reproduce Client and Admin Stop Failure Mapping

**Files:**
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs`

- [x] **Step 1: Write failing tests**

Add:

- `ClientStreamStopReturnsServiceUnavailableWhenBackendStopFails`
- `AdminStopSelectedClientStreamReturnsServiceUnavailableWhenBackendStopFails`

Each test injects a local `FailingStopStreamingBackend`, launches a normal session, calls the stop route, and expects `503` plus the backend error text.

- [x] **Step 2: Run focused red tests**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "ClientStreamStopReturnsServiceUnavailableWhenBackendStopFails|AdminStopSelectedClientStreamReturnsServiceUnavailableWhenBackendStopFails"
```

Expected before the fix: both tests fail because the routes return `NotFound`.

## Task 2: Preserve Backend Stop Failures

**Files:**
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`

- [x] **Step 1: Return 503 for stop failure after plan lookup succeeds**

In both routes:

```csharp
StreamingStopResult stop = await streaming.StopAsync(plan.SessionId, cancellationToken);
return stop.Success && stop.Session is not null
    ? Results.Ok(new { clientId, stream = stop.Session })
    : Results.Problem(stop.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
```

- [x] **Step 2: Run focused green verification**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "ClientStreamStopReturnsServiceUnavailableWhenBackendStopFails|AdminStopSelectedClientStreamReturnsServiceUnavailableWhenBackendStopFails|StreamStatusAndStopAreIndependentFromDisplayCleanup|AdminCanStopSelectedClientStream|AdminStopSelectedClientStreamReportsMissingSessionPlan"
```

Expected: all selected tests pass.

## Task 3: Documentation and Full Validation

**Files:**
- Modify: `README.md`

- [x] **Step 1: Update recovery/streaming documentation**

Clarify that stream stop returns `503` when the streaming backend cannot stop an existing planned session, while missing session plans remain `404`.

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
