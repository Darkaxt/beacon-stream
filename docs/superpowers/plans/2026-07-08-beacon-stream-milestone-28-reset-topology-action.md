# Milestone 28 Reset Topology Action Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an explicit WPF/server-admin reset-topology action that restores the physical primary display and moves virtual-display windows back minimized.

**Architecture:** Keep reset topology server-owned and composed from existing recovery boundaries. The admin endpoint calls `IDisplayBackend.RestorePhysicalPrimaryAsync` first, then `IRecoveryBackend.MoveWindowsBackAsync(minimize: true)`, and returns a single explicit result without adding new Windows API behavior.

**Tech Stack:** ASP.NET Core minimal APIs, WPF MVVM cockpit, xUnit integration and ViewModel tests.

---

### Task 1: Server Reset Topology Endpoint

**Files:**
- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs`
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`

- [x] **Step 1: Write the failing endpoint test**

Add a test near the existing admin recovery tests:

```csharp
[Fact]
public async Task AdminResetTopologyRestoresPhysicalAndMovesWindowsBackMinimized()
{
    var display = new FakeDisplayBackend();
    var recovery = new FakeRecoveryBackend
    {
        MoveWindowsBackResult = RecoveryActionResult.Ok("move-windows-back", 2, ["moved windows"])
    };
    WebApplicationFactory<Program> recoveryFactory = factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDisplayBackend>();
            services.RemoveAll<IRecoveryBackend>();
            services.AddSingleton<IDisplayBackend>(display);
            services.AddSingleton<IRecoveryBackend>(recovery);
        }));
    HttpClient client = recoveryFactory.CreateClient();

    HttpResponseMessage response = await client.PostAsJsonAsync("/admin/recovery/reset-topology", new { });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    JsonElement root = document.RootElement;
    Assert.True(root.GetProperty("success").GetBoolean());
    Assert.Equal("reset-topology", root.GetProperty("action").GetString());
    Assert.Equal(2, root.GetProperty("affectedCount").GetInt32());
    Assert.Equal(["physical-primary"], display.RestoreCalls);
    Assert.Equal([true], recovery.MoveWindowsBackCalls);
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "AdminResetTopologyRestoresPhysicalAndMovesWindowsBackMinimized"
```

Expected: FAIL because the endpoint is missing.

- [x] **Step 3: Implement the endpoint**

Add `/admin/recovery/reset-topology` to `AdminEndpoints`. If physical restore fails, return `503` with the restore error. If moving windows fails, return `503` with the move error. On success, return a `RecoveryActionResult.Ok("reset-topology", move.AffectedCount, diagnostics)`.

- [x] **Step 4: Run endpoint test to verify it passes**

Run the same focused test. Expected: PASS.

### Task 2: Cockpit API and ViewModel Wiring

**Files:**
- Modify: `tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs`
- Modify: `tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitApiClient.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs`
- Modify: `src/Beacon.Cockpit/MainWindow.xaml`

- [x] **Step 1: Write failing cockpit tests**

Update `SendsRecoveryRequestsToAdminEndpoints` to call `ResetTopologyAsync` and assert `/admin/recovery/reset-topology`. Update `RecoveryMethodsDelegateToServer` to call `ResetTopologyAsync` and assert the fake API flag.

- [x] **Step 2: Run cockpit tests to verify they fail**

Run:

```powershell
dotnet test tests\Beacon.Cockpit.Tests\Beacon.Cockpit.Tests.csproj --filter "SendsRecoveryRequestsToAdminEndpoints|RecoveryMethodsDelegateToServer"
```

Expected: FAIL because the API and command do not exist.

- [x] **Step 3: Implement cockpit wiring**

Add `Task ResetTopologyAsync(...)` to `ICockpitApi`, implement it in `CockpitApiClient`, add `ResetTopologyCommand` and `ResetTopologyAsync` to `CockpitShellViewModel`, update fake test APIs, and add a `Reset Topology` button in the Recovery tab and sidebar recovery group.

- [x] **Step 4: Run cockpit tests to verify they pass**

Run the same focused cockpit test command. Expected: PASS.

### Task 3: Docs, Validation, And Sync

**Files:**
- Modify: `README.md`

- [x] **Step 1: Document the recovery action**

Add `curl.exe -X POST http://localhost:5000/admin/recovery/reset-topology` to the recovery action list and mention that it restores physical primary before moving virtual-display windows back minimized.

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

Commit the milestone, push the branch, open a pull request, wait for CI, and merge only after CI is green.
