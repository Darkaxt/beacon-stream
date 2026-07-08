# Admin-Only Display Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the broad client-owned display recovery route so virtual display recovery/removal remains an admin/WPF action, while owning clients keep only the profile-gated emergency restore route.

**Architecture:** `/clients/{clientId}/emergency-restore` is the APK-safe recovery endpoint and is gated by `allowEmergencyRestoreFromClient`. `/admin/clients/{clientId}/display/recover` and `/admin/clients/{clientId}/display/remove` remain local-admin escape hatches. The older `/clients/{clientId}/display/recover` route is removed instead of gated, because its behavior restores physical primary and removes the client virtual display lease, which is broader than the APK emergency action.

**Tech Stack:** ASP.NET Core minimal APIs, existing `DisplayLeaseManager`, xUnit server integration tests.

---

## Requirements Covered

- `REQ-DISP-009`: manual recovery can override lifecycle rules only through WPF/admin or the owning APK emergency action.
- `REQ-REC-005`: APK may call emergency actions for its own active session.
- `REQ-REC-006`: WPF/admin may perform broader local admin recovery than the APK.
- `REQ-REC-007`: recovery remains an explicit escape hatch, not an uncontrolled client lifecycle substitute.
- `REQ-CTRL-012`: display destruction and recovery safety policies remain WPF/server-admin controlled in version 1.

## File Map

- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
  - Replace the client display recover happy-path test with a route-boundary test that expects 404.
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
  - Remove `POST /clients/{clientId}/display/recover`.
- Modify: `README.md`
  - Clarify that display lease recovery/removal is admin-only; APK uses emergency restore.
- Modify: this plan file
  - Track execution status.

## Task 1: Route Boundary Tests

**Files:**
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`

- [x] **Step 1: Replace the client recover happy-path test**

Replace `DisplayRecoverRunsManualRecoveryForClientLease` with:

```csharp
[Fact]
public async Task ClientDisplayRecoverRouteIsNotAvailableBecauseDisplayRecoveryIsAdminOnly()
{
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/display/recover", new { });

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
```

- [x] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientDisplayRecoverRouteIsNotAvailableBecauseDisplayRecoveryIsAdminOnly
```

Expected: fails with `Actual: OK` because the broad client route still exists.

## Task 2: Remove Client Display Recover Route

**Files:**
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`

- [x] **Step 1: Remove the route**

Delete this route block from `ClientEndpoints`:

```csharp
clients.MapPost("/{clientId}/display/recover", async (
    string clientId,
    DisplayLeaseManager leases,
    CancellationToken cancellationToken) =>
{
    string displayId = DisplayLease.CreateDisplayId(new ClientId(clientId));
    DisplayRecoveryResult result = await leases.RecoverDisplayAsync(displayId, cancellationToken);

    return result.Success
        ? Results.Ok(new { clientId, displayId, recovered = true })
        : Results.Problem(result.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
});
```

- [x] **Step 2: Verify green and admin coverage**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "ClientDisplayRecoverRouteIsNotAvailableBecauseDisplayRecoveryIsAdminOnly|AdminCanRequestPhysicalRestoreAndClientRecovery"
```

Expected: client route returns 404; existing admin recovery test remains green.

## Task 3: Docs, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: this plan file

- [x] **Step 1: Update README**

Revise the recovery paragraph to state:

```markdown
Owning-client emergency restore is profile-gated by `allowEmergencyRestoreFromClient`. Admin recovery endpoints remain broader local-admin escape hatches, including display lease recovery/removal.
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
git add src/Beacon.Server/Api/ClientEndpoints.cs tests/Beacon.Server.Tests/ClientApiTests.cs README.md docs/superpowers/plans/2026-07-08-beacon-stream-milestone-23-admin-only-display-recovery.md
git commit -m "Make display recovery admin-only"
git push -u origin codex/milestone-23-admin-only-display-recovery
gh pr create --draft --base main --head codex/milestone-23-admin-only-display-recovery --title "Make display recovery admin-only" --body "Milestone 23 removes the broad client display recovery route so APK recovery stays limited to the gated emergency restore endpoint."
```

Expected: branch is pushed and draft PR is open.

## Task 4: Refactor, Revalidate, And Merge

**Files:**
- Modify only files from Tasks 1-3 if cleanup is needed.

- [ ] **Step 1: Re-run focused validation after cleanup**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "ClientDisplayRecoverRouteIsNotAvailableBecauseDisplayRecoveryIsAdminOnly|AdminCanRequestPhysicalRestoreAndClientRecovery|EmergencyRestore"
```

Expected: route boundary, admin recovery, and emergency restore tests pass.

- [ ] **Step 2: Watch CI and merge**

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

- No change to admin recovery endpoints.
- No change to Android emergency restore UI.
- No new recovery action.
- No timeout or watchdog behavior.
