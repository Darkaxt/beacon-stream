# Milestone 27 Disconnect/Quit Stop Failure Handling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `/clients/{clientId}/disconnect` and `/clients/{clientId}/quit` report stream backend stop failures instead of silently continuing into misleading recovery state.

**Architecture:** Reuse the existing `StreamingStopResult` failure contract already used by `/clients/{clientId}/stream/stop`. `disconnect` still retains the display lease, while `quit` must stop before ownership lookup or lease cleanup when the stream backend refuses to stop.

**Tech Stack:** ASP.NET Core minimal APIs, xUnit integration tests, existing `FailingStopStreamingBackend`.

---

### Task 1: Disconnect Failure Contract

**Files:**
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`

- [x] **Step 1: Write the failing test**

Add an xUnit test near `DisconnectStopsStreamAndRetainsDisplayLease`:

```csharp
[Fact]
public async Task DisconnectReturnsServiceUnavailableWhenBackendStopFails()
{
    WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IStreamingBackend>();
            services.AddSingleton<IStreamingBackend>(new FailingStopStreamingBackend("wrapper refused stop"));
        }));
    HttpClient client = failingFactory.CreateClient();

    HttpResponseMessage launch = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
    HttpResponseMessage disconnect = await client.PostAsJsonAsync("/clients/z-fold-7/disconnect", new { });

    Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
    Assert.Equal(HttpStatusCode.ServiceUnavailable, disconnect.StatusCode);
    string body = await disconnect.Content.ReadAsStringAsync();
    Assert.Contains("wrapper refused stop", body, StringComparison.OrdinalIgnoreCase);
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "DisconnectReturnsServiceUnavailableWhenBackendStopFails"
```

Expected: FAIL because `/disconnect` currently returns `200 OK`.

- [x] **Step 3: Write minimal implementation**

In `/disconnect`, after `streaming.StopAsync`, return `Results.Problem(..., 503)` when `stop.Success` is false.

- [x] **Step 4: Run focused test to verify it passes**

Run the same focused test. Expected: PASS.

### Task 2: Quit Failure Contract

**Files:**
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`

- [x] **Step 1: Write the failing test**

Add an xUnit test near the quit lifecycle tests:

```csharp
[Fact]
public async Task QuitReturnsServiceUnavailableWhenBackendStopFailsAndSkipsDisplayCleanup()
{
    var display = new FakeDisplayBackend();
    WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDisplayBackend>();
            services.RemoveAll<IStreamingBackend>();
            services.AddSingleton<IDisplayBackend>(display);
            services.AddSingleton<IStreamingBackend>(new FailingStopStreamingBackend("wrapper refused stop"));
        }));
    HttpClient client = failingFactory.CreateClient();

    HttpResponseMessage launch = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
    HttpResponseMessage quit = await client.PostAsJsonAsync("/clients/z-fold-7/quit", new { clientActive = false });

    Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
    Assert.Equal(HttpStatusCode.ServiceUnavailable, quit.StatusCode);
    string body = await quit.Content.ReadAsStringAsync();
    Assert.Contains("wrapper refused stop", body, StringComparison.OrdinalIgnoreCase);
    Assert.Empty(display.RemoveCalls);
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "QuitReturnsServiceUnavailableWhenBackendStopFailsAndSkipsDisplayCleanup"
```

Expected: FAIL because `/quit` currently returns `200 OK` and continues cleanup.

- [x] **Step 3: Write minimal implementation**

In `/quit`, after `streaming.StopAsync`, return `Results.Problem(..., 503)` when `stop.Success` is false before taking the ownership snapshot or calling `CleanupIfAllowedAsync`.

- [x] **Step 4: Run focused lifecycle tests**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "DisconnectReturnsServiceUnavailableWhenBackendStopFails|QuitReturnsServiceUnavailableWhenBackendStopFailsAndSkipsDisplayCleanup|DisconnectStopsStreamAndRetainsDisplayLease|QuitIgnoresStaleClientOwnedWorkFlagsAndUsesServerSnapshot|QuitRetainsDisplayUntilServerOwnedWorkClears|StreamStatusAndStopAreIndependentFromDisplayCleanup"
```

Expected: PASS.

### Task 3: Validation and Sync

**Files:**
- Modify: `README.md`

- [x] **Step 1: Document the recovery contract**

Update the milestone summary to state that disconnect/quit now surface backend stop failures instead of hiding them.

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

Commit, push the milestone branch, open a pull request, wait for CI, and merge only after CI is green.
