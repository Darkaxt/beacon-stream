# Beacon Stream Milestone 4 WPF Cockpit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the local WPF cockpit for Beacon Stream administration: clients, sessions/displays, game library, planner decisions, recovery actions, and diagnostics.

**Architecture:** Keep WPF as a thin local admin surface over explicit server endpoints. Server state remains authoritative; the cockpit uses `HttpClient` plus testable ViewModels, and does not parse Steam/Heroic/Hydra files or touch display APIs directly. Recovery buttons call server actions and show the resulting state instead of duplicating lifecycle rules.

**Tech Stack:** .NET 10, WPF on `net10.0-windows`, ASP.NET minimal APIs, xUnit, built-in `HttpClient`, no extra MVVM package unless a concrete test proves it is needed.

---

## Scope

Milestone 4 implements a functional local cockpit, not the final polished product shell. It must be useful enough to pin and run locally, but the main success criteria are correct boundaries, recovery controls, and no-phone testability.

Included:

- Admin snapshot endpoint for clients, profiles, capabilities, telemetry, sessions, recovery state, and game count.
- Admin recovery endpoints for physical restore and per-client display recovery.
- WPF app with overview, clients/sessions, games, recovery, and diagnostics views.
- API client and ViewModels covered by fast tests.
- Documentation for running the cockpit.

Not included:

- Real streaming backend.
- Android APK.
- Full process/window enumeration beyond server state already exposed.
- Visual UI automation as a hard gate. Build plus ViewModel/API tests are the stable verification path.

## File Map

- Create: `src/Beacon.Cockpit/Beacon.Cockpit.csproj` - WPF app.
- Create: `src/Beacon.Cockpit/App.xaml` and `src/Beacon.Cockpit/App.xaml.cs` - app startup.
- Create: `src/Beacon.Cockpit/MainWindow.xaml` and `src/Beacon.Cockpit/MainWindow.xaml.cs` - local admin shell.
- Create: `src/Beacon.Cockpit/Cockpit/CockpitApiClient.cs` - typed HTTP client.
- Create: `src/Beacon.Cockpit/Cockpit/CockpitModels.cs` - DTOs matching admin endpoints.
- Create: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs` - overview state and commands.
- Create: `src/Beacon.Cockpit/Cockpit/RelayCommand.cs` - minimal command implementation.
- Create: `src/Beacon.Cockpit/Cockpit/ObservableObject.cs` - minimal property change base.
- Create: `tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj` - WPF ViewModel/API tests.
- Create: `tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs` - fake HTTP tests.
- Create: `tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs` - command/state tests.
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs` - new admin endpoints.
- Modify: `src/Beacon.Server/Program.cs` - map admin endpoints.
- Modify: `src/Beacon.Server/State/InMemoryClientStore.cs` - enumerate clients.
- Modify: `src/Beacon.Server/State/InMemorySessionStore.cs` - enumerate plans.
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs` or create `tests/Beacon.Server.Tests/AdminApiTests.cs` - admin endpoint coverage.
- Modify: `Beacon.slnx` - add cockpit app and tests.
- Modify: `README.md` - add cockpit run command.

## Task 1: Admin Snapshot Endpoint

**Files:**
- Create: `src/Beacon.Server/Api/AdminEndpoints.cs`
- Modify: `src/Beacon.Server/Program.cs`
- Modify: `src/Beacon.Server/State/InMemoryClientStore.cs`
- Modify: `src/Beacon.Server/State/InMemorySessionStore.cs`
- Test: `tests/Beacon.Server.Tests/AdminApiTests.cs`

- [x] **Step 1: Write failing admin snapshot test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Beacon.Server.Tests;

public sealed class AdminApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task SnapshotReturnsClientsGamesAndSessions()
    {
        HttpClient client = factory.CreateClient();

        await client.PostAsJsonAsync("/clients/z-fold-7/plan", new { gameId = "steam-shortcut:3767414131" });
        HttpResponseMessage response = await client.GetAsync("/admin/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;

        Assert.True(root.GetProperty("clients").GetArrayLength() > 0);
        Assert.True(root.GetProperty("games").GetProperty("total").GetInt32() > 0);
        Assert.Equal("z-fold-7", root.GetProperty("clients")[0].GetProperty("clientId").GetString());
        Assert.Equal("steam-shortcut:3767414131", root.GetProperty("sessions")[0].GetProperty("appId").GetString());
    }
}
```

- [x] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter AdminApiTests`

Expected: fail because `/admin/snapshot` is missing.

- [x] **Step 3: Add state enumeration**

Add to `InMemoryClientStore`:

```csharp
public IReadOnlyList<ClientProfile> GetProfiles() =>
    profiles.Values.OrderBy(profile => profile.ClientId.Value, StringComparer.OrdinalIgnoreCase).ToArray();
```

Add to `InMemorySessionStore`:

```csharp
public IReadOnlyList<SessionPlan> GetAll() =>
    plans.Values.OrderBy(plan => plan.ClientId.Value, StringComparer.OrdinalIgnoreCase).ToArray();
```

- [x] **Step 4: Add admin endpoint mapper**

Create `src/Beacon.Server/Api/AdminEndpoints.cs`:

```csharp
using Beacon.Core.Games;
using Beacon.Server.State;

namespace Beacon.Server.Api;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder admin = endpoints.MapGroup("/admin");

        admin.MapGet("/snapshot", async (
            InMemoryClientStore clients,
            InMemorySessionStore sessions,
            GameLibraryService games,
            CancellationToken cancellationToken) =>
        {
            GameLibrarySnapshot gameSnapshot = await games.ScanAsync(cancellationToken);
            return Results.Ok(new
            {
                clients = clients.GetProfiles().Select(profile => new
                {
                    clientId = profile.ClientId.Value,
                    profile,
                    capabilities = clients.GetCapabilities(profile.ClientId.Value),
                    telemetry = clients.GetTelemetry(profile.ClientId.Value)
                }),
                sessions = sessions.GetAll(),
                games = new
                {
                    total = gameSnapshot.Games.Count,
                    diagnostics = gameSnapshot.Diagnostics
                }
            });
        });

        return endpoints;
    }
}
```

Modify `Program.cs`:

```csharp
app.MapAdminEndpoints();
```

- [x] **Step 5: Verify green**

Run: `dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter AdminApiTests`

Expected: pass.

- [x] **Step 6: Commit**

```bash
git add src/Beacon.Server tests/Beacon.Server.Tests
git commit -m "Expose cockpit admin snapshot"
```

## Task 2: Admin Recovery Endpoints

**Files:**
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`
- Test: `tests/Beacon.Server.Tests/AdminApiTests.cs`

- [ ] **Step 1: Write failing recovery endpoint tests**

Add to `AdminApiTests`:

```csharp
[Fact]
public async Task AdminCanRequestPhysicalRestoreAndClientRecovery()
{
    HttpClient client = factory.CreateClient();

    HttpResponseMessage restore = await client.PostAsJsonAsync("/admin/recovery/restore-physical", new { });
    HttpResponseMessage recover = await client.PostAsJsonAsync("/admin/clients/z-fold-7/display/recover", new { });

    Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
    Assert.Equal(HttpStatusCode.OK, recover.StatusCode);

    using JsonDocument restoreJson = await JsonDocument.ParseAsync(await restore.Content.ReadAsStreamAsync());
    using JsonDocument recoverJson = await JsonDocument.ParseAsync(await recover.Content.ReadAsStreamAsync());

    Assert.True(restoreJson.RootElement.GetProperty("restoreRequested").GetBoolean());
    Assert.True(recoverJson.RootElement.GetProperty("recovered").GetBoolean());
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter AdminCanRequestPhysicalRestoreAndClientRecovery`

Expected: fail because admin recovery endpoints are missing.

- [ ] **Step 3: Implement endpoints**

Add to `MapAdminEndpoints`:

```csharp
admin.MapPost("/recovery/restore-physical", async (
    IDisplayBackend displayBackend,
    CancellationToken cancellationToken) =>
{
    await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
    return Results.Ok(new { restoreRequested = true });
});

admin.MapPost("/clients/{clientId}/display/recover", async (
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

Add required usings:

```csharp
using Beacon.Core.Clients;
using Beacon.Core.Displays;
```

- [ ] **Step 4: Verify green**

Run: `dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter AdminCanRequestPhysicalRestoreAndClientRecovery`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Beacon.Server tests/Beacon.Server.Tests
git commit -m "Add cockpit recovery endpoints"
```

## Task 3: Cockpit API Client

**Files:**
- Create: `src/Beacon.Cockpit/Beacon.Cockpit.csproj`
- Create: `src/Beacon.Cockpit/Cockpit/CockpitModels.cs`
- Create: `src/Beacon.Cockpit/Cockpit/CockpitApiClient.cs`
- Create: `tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj`
- Create: `tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs`
- Modify: `Beacon.slnx`

- [ ] **Step 1: Write failing API client tests**

```csharp
using System.Net;
using Beacon.Cockpit.Cockpit;

namespace Beacon.Cockpit.Tests;

public sealed class CockpitApiClientTests
{
    [Fact]
    public async Task LoadsSnapshotFromServer()
    {
        var handler = new FakeHttpHandler("""
        {
          "clients": [{ "clientId": "z-fold-7", "profile": {}, "capabilities": {}, "telemetry": {} }],
          "sessions": [{ "clientId": { "value": "z-fold-7" }, "appId": "steam-shortcut:3767414131" }],
          "games": { "total": 36, "diagnostics": ["Steam library 'G:\\SteamLibrary\\steamapps' does not exist."] }
        }
        """);
        var client = new CockpitApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") });

        CockpitSnapshot snapshot = await client.GetSnapshotAsync(CancellationToken.None);

        Assert.Single(snapshot.Clients);
        Assert.Equal("z-fold-7", snapshot.Clients[0].ClientId);
        Assert.Equal(36, snapshot.Games.Total);
        Assert.Single(snapshot.Games.Diagnostics);
    }
}
```

Include a fake handler in the test file:

```csharp
private sealed class FakeHttpHandler(string responseBody) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody)
        });
    }
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj`

Expected: fail because the cockpit project does not exist.

- [ ] **Step 3: Create WPF project and models**

Create `src/Beacon.Cockpit/Beacon.Cockpit.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

Create `tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj` with `TargetFramework` `net10.0-windows`, xUnit packages matching other tests, and a project reference to `src/Beacon.Cockpit`.

Create `CockpitModels.cs`:

```csharp
namespace Beacon.Cockpit.Cockpit;

public sealed record CockpitSnapshot(
    IReadOnlyList<CockpitClientSummary> Clients,
    IReadOnlyList<CockpitSessionSummary> Sessions,
    CockpitGameSummary Games);

public sealed record CockpitClientSummary(string ClientId);

public sealed record CockpitSessionSummary(string AppId);

public sealed record CockpitGameSummary(int Total, IReadOnlyList<string> Diagnostics);

public sealed record CockpitRecoveryResult(bool RestoreRequested, bool Recovered, string? DisplayId);
```

- [ ] **Step 4: Implement API client**

Create `CockpitApiClient.cs`:

```csharp
using System.Net.Http.Json;

namespace Beacon.Cockpit.Cockpit;

public sealed class CockpitApiClient(HttpClient httpClient)
{
    public async Task<CockpitSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        CockpitSnapshot? snapshot = await httpClient.GetFromJsonAsync<CockpitSnapshot>("/admin/snapshot", cancellationToken);
        return snapshot ?? new CockpitSnapshot([], [], new CockpitGameSummary(0, []));
    }

    public async Task RestorePhysicalAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("/admin/recovery/restore-physical", new { }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task RecoverClientDisplayAsync(string clientId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync($"/admin/clients/{Uri.EscapeDataString(clientId)}/display/recover", new { }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 5: Verify green**

Run: `dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj`

Expected: pass.

- [ ] **Step 6: Commit**

```bash
git add Beacon.slnx src/Beacon.Cockpit tests/Beacon.Cockpit.Tests
git commit -m "Add cockpit API client"
```

## Task 4: Cockpit ViewModels

**Files:**
- Create: `src/Beacon.Cockpit/Cockpit/ObservableObject.cs`
- Create: `src/Beacon.Cockpit/Cockpit/RelayCommand.cs`
- Create: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs`
- Test: `tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs`

- [ ] **Step 1: Write failing ViewModel tests**

```csharp
using Beacon.Cockpit.Cockpit;

namespace Beacon.Cockpit.Tests;

public sealed class CockpitShellViewModelTests
{
    [Fact]
    public async Task RefreshPopulatesDashboardState()
    {
        var api = new FakeCockpitApi(new CockpitSnapshot(
            [new CockpitClientSummary("z-fold-7")],
            [new CockpitSessionSummary("steam-shortcut:3767414131")],
            new CockpitGameSummary(36, ["Steam library stale"])));
        var viewModel = new CockpitShellViewModel(api);

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal(1, viewModel.ClientCount);
        Assert.Equal(1, viewModel.SessionCount);
        Assert.Equal(36, viewModel.GameCount);
        Assert.Contains("Steam library stale", viewModel.Diagnostics);
    }

    [Fact]
    public async Task RecoveryCommandsDelegateToServer()
    {
        var api = new FakeCockpitApi(new CockpitSnapshot([], [], new CockpitGameSummary(0, [])));
        var viewModel = new CockpitShellViewModel(api) { SelectedClientId = "z-fold-7" };

        await viewModel.RestorePhysicalAsync(CancellationToken.None);
        await viewModel.RecoverSelectedClientAsync(CancellationToken.None);

        Assert.True(api.RestorePhysicalCalled);
        Assert.Equal("z-fold-7", api.RecoveredClientId);
    }
}
```

The fake API must implement an interface:

```csharp
private sealed class FakeCockpitApi(CockpitSnapshot snapshot) : ICockpitApi
{
    public bool RestorePhysicalCalled { get; private set; }
    public string? RecoveredClientId { get; private set; }

    public Task<CockpitSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    public Task RestorePhysicalAsync(CancellationToken cancellationToken) { RestorePhysicalCalled = true; return Task.CompletedTask; }
    public Task RecoverClientDisplayAsync(string clientId, CancellationToken cancellationToken) { RecoveredClientId = clientId; return Task.CompletedTask; }
}
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj --filter CockpitShellViewModelTests`

Expected: fail because ViewModel types are missing.

- [ ] **Step 3: Add API interface and ViewModel**

Add `ICockpitApi` to `CockpitApiClient.cs` and implement it:

```csharp
public interface ICockpitApi
{
    Task<CockpitSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    Task RestorePhysicalAsync(CancellationToken cancellationToken);
    Task RecoverClientDisplayAsync(string clientId, CancellationToken cancellationToken);
}
```

Create `ObservableObject`, `RelayCommand`, and `CockpitShellViewModel` with these public properties:

```csharp
public int ClientCount { get; private set; }
public int SessionCount { get; private set; }
public int GameCount { get; private set; }
public string SelectedClientId { get; set; } = string.Empty;
public ObservableCollection<string> Clients { get; } = [];
public ObservableCollection<string> Sessions { get; } = [];
public ObservableCollection<string> Diagnostics { get; } = [];
public ICommand RefreshCommand { get; }
public ICommand RestorePhysicalCommand { get; }
public ICommand RecoverSelectedClientCommand { get; }
```

Commands must call async methods and surface errors through:

```csharp
public string StatusMessage { get; private set; } = "Ready.";
```

Do not use cancellation timeouts. Use the command invocation cancellation token supplied by tests or `CancellationToken.None` from UI commands.

- [ ] **Step 4: Verify green**

Run: `dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj --filter CockpitShellViewModelTests`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Beacon.Cockpit tests/Beacon.Cockpit.Tests
git commit -m "Add cockpit shell view model"
```

## Task 5: WPF Shell UI

**Files:**
- Create: `src/Beacon.Cockpit/App.xaml`
- Create: `src/Beacon.Cockpit/App.xaml.cs`
- Create: `src/Beacon.Cockpit/MainWindow.xaml`
- Create: `src/Beacon.Cockpit/MainWindow.xaml.cs`
- Modify: `src/Beacon.Cockpit/Beacon.Cockpit.csproj`

- [ ] **Step 1: Add startup wiring**

`App.xaml.cs` must parse an optional `--server` argument. Default to `http://localhost:5000`. It creates `HttpClient`, `CockpitApiClient`, `CockpitShellViewModel`, and `MainWindow`.

- [ ] **Step 2: Add shell XAML**

`MainWindow.xaml` must be a dense local admin tool:

- Top bar: server URL, refresh button, status text.
- Left summary: client/session/game counters.
- Main tabs: `Clients`, `Games`, `Recovery`, `Diagnostics`.
- Clients tab: client list and active session ids.
- Games tab: game count and note that detailed game browsing is server-provided through `/games`.
- Recovery tab: restore physical display and recover selected client display buttons.
- Diagnostics tab: provider diagnostics list.

No marketing hero, no decorative art, no nested cards. Use a restrained Windows admin style.

- [ ] **Step 3: Bind controls**

Bind buttons to `RefreshCommand`, `RestorePhysicalCommand`, and `RecoverSelectedClientCommand`. Bind client selector to `SelectedClientId`, lists to `Clients`, `Sessions`, and `Diagnostics`, and counters to `ClientCount`, `SessionCount`, `GameCount`.

- [ ] **Step 4: Build cockpit**

Run: `dotnet build src/Beacon.Cockpit/Beacon.Cockpit.csproj -warnaserror`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Beacon.Cockpit
git commit -m "Add WPF cockpit shell"
```

## Task 6: Docs And Validation

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md` if ApolloDisplayRescue is used as implementation reference.
- Modify: `docs/superpowers/plans/2026-07-07-beacon-stream-milestone-4-wpf-cockpit.md`

- [ ] **Step 1: Update docs**

Add:

```powershell
dotnet run --project src/Beacon.Cockpit -- --server http://localhost:5000
```

Document that the cockpit calls server endpoints and does not directly operate on display drivers.

- [ ] **Step 2: Static validation**

Run:

```bash
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
```

Expected: pass.

- [ ] **Step 3: Dynamic validation**

Run:

```bash
dotnet test Beacon.slnx
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright lint
pnpm --dir tests/Beacon.ClientLab.Playwright test
dotnet run --project src/Beacon.GameProbe -- scan --json
```

Expected: pass. GameProbe still reports normalized local game data and any stale local Steam diagnostics.

- [ ] **Step 4: Commit**

```bash
git add README.md docs src tests Beacon.slnx
git commit -m "Document WPF cockpit milestone"
```

## Task 7: Refactor Review And PR

**Files:**
- Same files touched above.

- [ ] **Step 1: Boundary audit**

Run:

```bash
rg "Sudo|DisplayConfig|SteamGameLibraryProvider|HydraGameLibraryProvider|HeroicGameLibraryProvider" src/Beacon.Cockpit -n
rg "Thread.Sleep|Task.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\\(" src tests -n
```

Expected:

- First command has no matches. The cockpit must call server APIs, not lower-level display or provider implementations.
- Second command has no cancellation timeout patterns introduced by Milestone 4.

- [ ] **Step 2: Repeat validation**

Run:

```bash
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
dotnet test Beacon.slnx
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright lint
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Expected: pass.

- [ ] **Step 3: Push and open PR**

```bash
git status --short
git push origin codex/milestone-4-wpf-cockpit
gh pr create --draft --base main --head codex/milestone-4-wpf-cockpit --title "Implement WPF cockpit" --body "Milestone 4 WPF cockpit implementation."
gh pr checks --watch
```

Expected: CI passes. Mark the PR ready after CI is green.

## Requirement Coverage

- `REQ-CTRL-010`: WPF cockpit exists as local server-admin surface.
- `REQ-CTRL-012`: display/recovery policies remain server-admin controlled.
- `REQ-MODE-004`: WPF has emergency restore surface.
- `REQ-DISP-009`: manual recovery can override lifecycle through explicit cockpit action.
- `REQ-DISP-014` through `REQ-DISP-016`: cockpit exposes restore/recover actions and diagnostics; deeper reconciliation remains server/display backend responsibility.
- `REQ-GAME-001`: cockpit shows game library summary.
- `REQ-TEST-007` through `REQ-TEST-010`: cockpit is testable without phone through API client, ViewModel, server API, and existing local probes.

## Plan Self-Review

- Placeholder scan: no placeholder task bodies; every task has files, commands, and expected results.
- Scope check: this milestone is only the WPF cockpit/admin surface. Streaming backend and APK remain later milestones.
- Boundary check: WPF calls explicit server endpoints and does not parse provider files or call display APIs directly.
- Timeout check: plan explicitly avoids cancellation timeouts and uses deterministic tests.
