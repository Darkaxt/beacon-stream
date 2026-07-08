# Emergency Restore Policy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make owning-client emergency restore honor the client profile policy and report physical-restore failure instead of always returning success.

**Architecture:** Keep broad recovery under `/admin`; only the owning-client `/clients/{clientId}/emergency-restore` route is constrained by `ClientProfile.Session.AllowEmergencyRestoreFromClient`. The route must load the profile, reject unknown clients, reject clients whose profile disallows emergency restore, call `IDisplayBackend.RestorePhysicalPrimaryAsync` only when allowed, and return 503 if the restore backend cannot verify recovery.

**Tech Stack:** ASP.NET Core minimal APIs, existing `InMemoryClientStore`, `IDisplayBackend`, xUnit integration tests.

---

## Requirements Covered

- `REQ-DISP-009`: manual recovery can be requested by WPF or by the owning APK only when allowed.
- `REQ-MODE-004`: owning APK recovery exists for blackout/remote-control scenarios, but it must be recoverable and policy-bound.
- `REQ-REC-005`: APK may call emergency actions for its own active session.
- `REQ-REC-006`: WPF/admin remains broader than APK recovery.
- `REQ-REC-009`: root fixes and safe failure handling are preferred over nicer error messages alone.

## File Map

- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
  - Add policy-denied and backend-failure coverage for `/clients/{clientId}/emergency-restore`.
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
  - Load profile, enforce `AllowEmergencyRestoreFromClient`, and return restore failure as 503.
- Modify: `README.md`
  - Document that owning-client emergency restore is profile-gated while admin recovery stays broader.
- Modify: this plan file
  - Track execution status.

## Task 1: Failing Server Tests

**Files:**
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`

- [x] **Step 1: Add policy-denied test**

Add to `ClientApiTests`:

```csharp
[Fact]
public async Task EmergencyRestoreRejectsClientWhenProfileDisallowsIt()
{
    HttpClient client = factory.CreateClient();
    HttpResponseMessage patch = await client.PatchAsJsonAsync("/admin/clients/z-fold-7/profile", new
    {
        allowEmergencyRestoreFromClient = false
    });

    HttpResponseMessage restore = await client.PostAsJsonAsync("/clients/z-fold-7/emergency-restore", new { });

    Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, restore.StatusCode);
    string body = await restore.Content.ReadAsStringAsync();
    Assert.Contains("emergency restore", body, StringComparison.OrdinalIgnoreCase);
}
```

- [x] **Step 2: Add backend-failure test**

Add to `ClientApiTests`:

```csharp
[Fact]
public async Task EmergencyRestoreReturnsServiceUnavailableWhenPhysicalRestoreFails()
{
    var display = new FakeDisplayBackend
    {
        NextRestoreResult = DisplayRestoreResult.Fail("physical primary was not verified")
    };
    WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDisplayBackend>();
            services.AddSingleton<IDisplayBackend>(display);
        }));
    HttpClient client = failingFactory.CreateClient();

    HttpResponseMessage restore = await client.PostAsJsonAsync("/clients/z-fold-7/emergency-restore", new { });

    Assert.Equal(HttpStatusCode.ServiceUnavailable, restore.StatusCode);
    string body = await restore.Content.ReadAsStringAsync();
    Assert.Contains("physical primary was not verified", body, StringComparison.OrdinalIgnoreCase);
    Assert.Equal("physical-primary", Assert.Single(display.RestoreCalls));
}
```

- [x] **Step 3: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "EmergencyRestoreRejectsClientWhenProfileDisallowsIt|EmergencyRestoreReturnsServiceUnavailableWhenPhysicalRestoreFails"
```

Expected: tests fail because the endpoint currently does not load the profile policy and ignores restore failure.

## Task 2: Endpoint Policy And Failure Handling

**Files:**
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`

- [x] **Step 1: Implement endpoint policy**

Replace the emergency restore endpoint body with:

```csharp
clients.MapPost("/{clientId}/emergency-restore", async (
    string clientId,
    InMemoryClientStore clients,
    IDisplayBackend displayBackend,
    CancellationToken cancellationToken) =>
{
    ClientProfile? profile = clients.GetProfile(clientId);
    if (profile is null)
    {
        return Results.NotFound(new { error = $"Client '{clientId}' is not registered." });
    }

    if (!profile.Session.AllowEmergencyRestoreFromClient)
    {
        return Results.Problem(
            "Emergency restore is disabled for this client profile.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    DisplayRestoreResult restore = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
    if (!restore.Success)
    {
        return Results.Problem(
            restore.Error ?? "Physical primary restore failed.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new { clientId, restoreRequested = true });
});
```

- [x] **Step 2: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "EmergencyRestoreRejectsClientWhenProfileDisallowsIt|EmergencyRestoreReturnsServiceUnavailableWhenPhysicalRestoreFails|DisconnectQuitAndEmergencyRestoreReturnExplicitRecoveryState"
```

Expected: new tests and existing happy-path emergency restore test pass.

## Task 3: Docs, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: this plan file

- [x] **Step 1: Update README**

Add near recovery actions:

```markdown
Owning-client emergency restore is profile-gated by `allowEmergencyRestoreFromClient`. Admin recovery endpoints remain broader local-admin escape hatches.
```

- [x] **Step 2: Static validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
```

Expected: both pass.

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

Expected: all pass; existing Gradle deprecation warnings are acceptable.

- [x] **Step 4: Boundary audit**

Run:

```powershell
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
```

Expected: no matches introduced.

- [ ] **Step 5: Sync**

Run:

```powershell
git add src/Beacon.Server/Api/ClientEndpoints.cs tests/Beacon.Server.Tests/ClientApiTests.cs README.md docs/superpowers/plans/2026-07-08-beacon-stream-milestone-22-emergency-restore-policy.md
git commit -m "Enforce client emergency restore policy"
git push -u origin codex/milestone-22-emergency-restore-policy
gh pr create --draft --base main --head codex/milestone-22-emergency-restore-policy --title "Enforce client emergency restore policy" --body "Milestone 22 gates owning-client emergency restore by profile policy and surfaces restore failure."
```

Expected: branch is pushed and draft PR is open.

## Task 4: Refactor, Revalidate, And Merge

**Files:**
- Modify only files from Tasks 1-3 if cleanup is needed.

- [ ] **Step 1: Refactor after green**

Only extract response helpers if the endpoint body becomes duplicated with another client-owned recovery endpoint. Do not change admin recovery scope.

- [ ] **Step 2: Re-run focused validation**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter EmergencyRestore
```

Expected: all emergency restore tests pass.

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

- No changes to `/admin/recovery/*` permissions or scope.
- No new recovery action.
- No Android UI changes; the APK already calls the owning-client emergency endpoint.
- No timeout or watchdog behavior.
