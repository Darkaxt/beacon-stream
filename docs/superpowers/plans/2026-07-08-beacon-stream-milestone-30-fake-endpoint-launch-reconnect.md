# Milestone 30 Fake Endpoint Launch/Reconnect Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the CLI fake endpoint exercise launch and reconnect so scripted no-phone validation matches the approved client lifecycle contract.

**Architecture:** Keep `FakeEndpointRunner` as the single deterministic CLI script runner. Extend its ordered request sequence to call `/clients/{clientId}/launch` after the first plan and `/clients/{clientId}/reconnect` after disconnect, using the existing game selection payload.

**Tech Stack:** .NET, xUnit, `HttpClient` fake handler tests.

---

### Task 1: Script Sequence Parity

**Files:**
- Modify: `tests/Beacon.FakeEndpoint.Tests/FakeEndpointRunnerTests.cs`
- Modify: `src/Beacon.FakeEndpoint/FakeEndpointRunner.cs`

- [x] **Step 1: Write the failing order test**

Update `RunsZFoldControlPlaneScriptInOrder` so it expects launch and reconnect:

```csharp
Assert.Equal(
    [
        "POST /clients/hello",
        "GET /clients/z-fold-7/profile",
        "PATCH /clients/z-fold-7/profile",
        "POST /clients/z-fold-7/capabilities",
        "POST /clients/z-fold-7/telemetry",
        "POST /clients/z-fold-7/plan",
        "POST /clients/z-fold-7/launch",
        "POST /clients/z-fold-7/disconnect",
        "POST /clients/z-fold-7/reconnect",
        "POST /clients/z-fold-7/plan",
        "POST /clients/z-fold-7/quit",
        "POST /clients/z-fold-7/emergency-restore"
    ],
    handler.Requests);
```

- [x] **Step 2: Run the focused test to verify it fails**

Run:

```powershell
dotnet test tests\Beacon.FakeEndpoint.Tests\Beacon.FakeEndpoint.Tests.csproj --filter RunsZFoldControlPlaneScriptInOrder
```

Expected: FAIL because the current fake endpoint does not call `/launch` or `/reconnect`.

- [x] **Step 3: Implement the minimal runner sequence**

Change `FakeEndpointRunner.RunAsync` so the `ok` chain includes:

```csharp
await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/launch", CreatePlanRequest(script), operations, cancellationToken) &&
await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/disconnect", new { }, operations, cancellationToken) &&
await SendAsync(HttpMethod.Post, $"/clients/{script.ClientId}/reconnect", new { }, operations, cancellationToken) &&
```

- [x] **Step 4: Run focused fake endpoint tests**

Run:

```powershell
dotnet test tests\Beacon.FakeEndpoint.Tests\Beacon.FakeEndpoint.Tests.csproj
```

Expected: PASS.

### Task 2: Documentation And Validation

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-30-fake-endpoint-launch-reconnect.md`

- [x] **Step 1: Document fake endpoint parity**

Update the fake endpoint README paragraph to state it runs the no-phone script through hello, profile fetch/patch, capabilities, telemetry, plan, launch, disconnect, reconnect, quit, and emergency restore.

- [x] **Step 2: Run full validation**

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

- [ ] **Step 3: Commit and sync**

Commit, push, open a pull request, wait for CI, and merge only after CI is green.
