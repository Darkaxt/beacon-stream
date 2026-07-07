# Beacon Stream Milestone 5 Streaming Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a server-owned streaming backend boundary so Beacon can start, stop, report, and recover stream sessions from a computed session plan without coupling display lifecycle to the streaming engine.

**Architecture:** Keep stream policy in `Beacon.Core`, wire orchestration through `Beacon.Server`, and keep native backend integration behind an `IStreamingBackend` interface. The first backend is deterministic and fake for no-phone validation; the real Sunshine-compatible boundary is represented as an external process adapter and reference-audited before any upstream code is copied or adapted.

**Tech Stack:** .NET 10, ASP.NET minimal APIs, xUnit, WPF cockpit DTO/ViewModel updates, existing Client Lab and Playwright validation, GitHub Actions.

---

## Upstream Reference Notes

Sunshine remains the primary mature reference for streaming primitives. Current upstream docs/repo show that Sunshine is a Moonlight-compatible host with hardware and software encoding support, plus Windows capture/encoding capabilities such as DXGI Desktop Duplication, Windows Graphics Capture, NVENC, AMF, QuickSync, Media Foundation, and software encoding. Sunshine also exposes prep-command and input configuration concepts, but Beacon must not rely on prep commands to manage display topology because Beacon already owns display lifecycle before launch. The relevant GameStream/NVHTTP launch surface lives in Sunshine's `src/nvhttp.cpp`; Beacon should wrap backend behavior rather than copy that route into server policy.

Reference links:

- `https://github.com/LizardByte/Sunshine`
- `https://docs.lizardbyte.dev/projects/sunshine/v0.23.0/about/advanced_usage.html`
- `https://github.com/LizardByte/Sunshine/blob/master/src/nvhttp.cpp`
- `https://github.com/moonlight-stream/moonlight-docs/wiki/Frequently-Asked-Questions`

## Scope

Included:

- Streaming backend contracts in `Beacon.Core`.
- Deterministic fake streaming backend for fast tests.
- Server launch path starts stream only after virtual display lease succeeds.
- Stream stop is independent from display cleanup.
- Disconnect stops stream while retaining display lease.
- Quit stops stream before evaluating display cleanup.
- Admin snapshot and WPF cockpit expose active stream state.
- Client Lab and Playwright flow show the new `streaming` launch state.
- External process backend boundary for a future Sunshine-compatible executable or wrapper script.
- Documentation of the no-copy Sunshine boundary.

Not included:

- Copying Sunshine C++ source.
- Implementing RTSP/GameStream protocol in C#.
- Building the Android APK.
- Real Moonlight decoder/client validation.
- Real native capture/encode quality testing.

## File Map

- Create: `src/Beacon.Core/Streaming/IStreamingBackend.cs` - backend interface and result contracts.
- Create: `src/Beacon.Core/Streaming/StreamingSessionState.cs` - immutable session state DTOs.
- Create: `src/Beacon.Core/Streaming/FakeStreamingBackend.cs` - deterministic backend used by tests and default server wiring.
- Create: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs` - process-backed adapter boundary that receives a session plan and starts/stops an external backend command.
- Create: `tests/Beacon.Core.Tests/Streaming/FakeStreamingBackendTests.cs` - fast backend contract tests.
- Create: `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs` - command-building tests, no real Sunshine launch.
- Modify: `src/Beacon.Server/Program.cs` - register streaming backend.
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs` - stream launch/stop/status lifecycle.
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs` - include stream states in admin snapshot.
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs` - API behavior coverage.
- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs` - admin snapshot stream coverage.
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitModels.cs` - stream DTOs.
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs` - stream count/list.
- Modify: `src/Beacon.Cockpit/MainWindow.xaml` - stream tab/summary.
- Modify: `tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs` - snapshot parsing.
- Modify: `tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs` - stream state projection.
- Modify: `src/Beacon.ClientLab/src/clientLab.ts` - launch response state.
- Modify: `src/Beacon.ClientLab/src/main.ts` - render streaming state.
- Modify: `src/Beacon.ClientLab/src/clientLab.test.ts` - unit flow.
- Modify: `tests/Beacon.ClientLab.Playwright/tests/client-lab.spec.ts` - browser flow.
- Modify: `docs/extraction-map.md` - keep Sunshine marked reference-only unless code is copied.
- Modify: `README.md` - streaming backend notes and validation command.

## Task 1: Streaming Contracts And Fake Backend

**Files:**
- Create: `src/Beacon.Core/Streaming/IStreamingBackend.cs`
- Create: `src/Beacon.Core/Streaming/StreamingSessionState.cs`
- Create: `src/Beacon.Core/Streaming/FakeStreamingBackend.cs`
- Test: `tests/Beacon.Core.Tests/Streaming/FakeStreamingBackendTests.cs`

- [x] **Step 1: Write failing fake backend tests**

Create `tests/Beacon.Core.Tests/Streaming/FakeStreamingBackendTests.cs`:

```csharp
using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Sessions;
using Beacon.Core.Streaming;

namespace Beacon.Core.Tests.Streaming;

public sealed class FakeStreamingBackendTests
{
    [Fact]
    public async Task StartCreatesRunningSessionFromPlan()
    {
        var backend = new FakeStreamingBackend();
        SessionPlan plan = CreatePlan();

        StreamingStartResult result = await backend.StartAsync(plan, CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.Equal("z-fold-7-steam-shortcut:3767414131", session.SessionId);
        Assert.Equal("z-fold-7", session.ClientId);
        Assert.Equal("client-z-fold-7", session.DisplayId);
        Assert.Equal("av1", session.Codec);
        Assert.Equal(120, session.Fps);
        Assert.Equal("running", session.State);
        Assert.Single(backend.GetSessions());
    }

    [Fact]
    public async Task StopMarksSessionStoppedWithoutDeletingState()
    {
        var backend = new FakeStreamingBackend();
        SessionPlan plan = CreatePlan();
        await backend.StartAsync(plan, CancellationToken.None);

        StreamingStopResult result = await backend.StopAsync(plan.SessionId, CancellationToken.None);

        Assert.True(result.Success);
        StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
        Assert.Equal("stopped", session.State);
        Assert.Equal("client-z-fold-7", session.DisplayId);
        Assert.Equal(session, await backend.GetSessionAsync(plan.SessionId, CancellationToken.None));
    }

    [Fact]
    public async Task StartFailureReturnsDiagnostic()
    {
        var backend = new FakeStreamingBackend { NextStartError = "encoder unavailable" };

        StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Session);
        Assert.Equal("encoder unavailable", result.Error);
        Assert.Empty(backend.GetSessions());
    }

    private static SessionPlan CreatePlan() =>
        new(
            SessionId: "z-fold-7-steam-shortcut:3767414131",
            ClientId: new ClientId("z-fold-7"),
            AppId: "steam-shortcut:3767414131",
            Display: new PlannedDisplay(
                "client-z-fold-7",
                2560,
                1600,
                120,
                "virtual-primary",
                HdrPreference.Prefer,
                HdrEnabled: false,
                "sdr",
                "HDR disabled because virtual display does not report HDR capability."),
            Stream: new PlannedStream("av1", 120, 65, "lan-direct", "adaptive"));
}
```

- [x] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter FakeStreamingBackendTests
```

Expected: compile failure because `Beacon.Core.Streaming` does not exist.

- [x] **Step 3: Add streaming contracts**

Create `src/Beacon.Core/Streaming/StreamingSessionState.cs`:

```csharp
namespace Beacon.Core.Streaming;

public sealed record StreamingSessionState(
    string SessionId,
    string ClientId,
    string AppId,
    string DisplayId,
    string Codec,
    int Fps,
    int InitialBitrateMbps,
    string Transport,
    string State,
    string? Error);
```

Create `src/Beacon.Core/Streaming/IStreamingBackend.cs`:

```csharp
using Beacon.Core.Sessions;

namespace Beacon.Core.Streaming;

public interface IStreamingBackend
{
    Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken);

    Task<StreamingStopResult> StopAsync(string sessionId, CancellationToken cancellationToken);

    Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken);

    IReadOnlyList<StreamingSessionState> GetSessions();
}

public sealed record StreamingStartResult(bool Success, StreamingSessionState? Session, string? Error)
{
    public static StreamingStartResult Ok(StreamingSessionState session) => new(true, session, null);

    public static StreamingStartResult Fail(string error) => new(false, null, error);
}

public sealed record StreamingStopResult(bool Success, StreamingSessionState? Session, string? Error)
{
    public static StreamingStopResult Ok(StreamingSessionState session) => new(true, session, null);

    public static StreamingStopResult Fail(string error) => new(false, null, error);
}
```

- [x] **Step 4: Add fake backend**

Create `src/Beacon.Core/Streaming/FakeStreamingBackend.cs`:

```csharp
using Beacon.Core.Sessions;

namespace Beacon.Core.Streaming;

public sealed class FakeStreamingBackend : IStreamingBackend
{
    private readonly Dictionary<string, StreamingSessionState> sessions = new(StringComparer.OrdinalIgnoreCase);

    public string? NextStartError { get; set; }

    public List<string> StartCalls { get; } = [];

    public List<string> StopCalls { get; } = [];

    public Task<StreamingStartResult> StartAsync(SessionPlan plan, CancellationToken cancellationToken)
    {
        StartCalls.Add(plan.SessionId);

        if (!string.IsNullOrWhiteSpace(NextStartError))
        {
            string error = NextStartError;
            NextStartError = null;
            return Task.FromResult(StreamingStartResult.Fail(error));
        }

        var session = new StreamingSessionState(
            plan.SessionId,
            plan.ClientId.Value,
            plan.AppId,
            plan.Display.DisplayId,
            plan.Stream.Codec,
            plan.Stream.Fps,
            plan.Stream.InitialBitrateMbps,
            plan.Stream.Transport,
            State: "running",
            Error: null);

        sessions[plan.SessionId] = session;
        return Task.FromResult(StreamingStartResult.Ok(session));
    }

    public Task<StreamingStopResult> StopAsync(string sessionId, CancellationToken cancellationToken)
    {
        StopCalls.Add(sessionId);

        if (!sessions.TryGetValue(sessionId, out StreamingSessionState? session))
        {
            return Task.FromResult(StreamingStopResult.Fail($"Stream session '{sessionId}' is not running."));
        }

        StreamingSessionState stopped = session with { State = "stopped" };
        sessions[sessionId] = stopped;
        return Task.FromResult(StreamingStopResult.Ok(stopped));
    }

    public Task<StreamingSessionState?> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(sessions.GetValueOrDefault(sessionId));

    public IReadOnlyList<StreamingSessionState> GetSessions() =>
        sessions.Values
            .OrderBy(session => session.ClientId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
```

- [x] **Step 5: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter FakeStreamingBackendTests
```

Expected: all fake backend tests pass.

- [x] **Step 6: Commit**

```powershell
git add src/Beacon.Core/Streaming tests/Beacon.Core.Tests/Streaming
git commit -m "Add streaming backend contract"
git push
```

## Task 2: Launch Starts Stream After Display Lease

**Files:**
- Modify: `src/Beacon.Server/Program.cs`
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Test: `tests/Beacon.Server.Tests/ClientApiTests.cs`

- [x] **Step 1: Write failing launch tests**

Add these usings to `tests/Beacon.Server.Tests/ClientApiTests.cs`:

```csharp
using Beacon.Core.Streaming;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
```

Add these tests to `ClientApiTests`:

```csharp
[Fact]
public async Task LaunchStartsStreamingBackendWithSessionPlan()
{
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
    {
        gameId = "steam-shortcut:3767414131"
    });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    JsonElement root = document.RootElement;

    Assert.Equal("streaming", root.GetProperty("state").GetString());
    Assert.Equal("client-z-fold-7", root.GetProperty("displayId").GetString());
    Assert.Equal("running", root.GetProperty("stream").GetProperty("state").GetString());
    Assert.Equal("av1", root.GetProperty("stream").GetProperty("codec").GetString());
    Assert.Equal(120, root.GetProperty("stream").GetProperty("fps").GetInt32());
}

[Fact]
public async Task LaunchSurfacesStreamingStartFailureAndRestoresPhysicalPrimary()
{
    var backend = new FakeStreamingBackend { NextStartError = "encoder unavailable" };
    WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IStreamingBackend>();
            services.AddSingleton<IStreamingBackend>(backend);
        }));
    HttpClient client = failingFactory.CreateClient();

    HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
    {
        gameId = "steam-shortcut:3767414131"
    });

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    string body = await response.Content.ReadAsStringAsync();
    Assert.Contains("encoder unavailable", body, StringComparison.OrdinalIgnoreCase);
}
```

- [x] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "LaunchStartsStreamingBackendWithSessionPlan|LaunchSurfacesStreamingStartFailureAndRestoresPhysicalPrimary"
```

Expected: fail because the server does not register or call `IStreamingBackend`, and launch still returns `state = started`.

- [x] **Step 3: Register the streaming backend**

Modify `src/Beacon.Server/Program.cs`:

```csharp
using Beacon.Core.Streaming;
```

Add the service registration after `DisplayLeaseManager`:

```csharp
builder.Services.AddSingleton<IStreamingBackend, FakeStreamingBackend>();
```

- [x] **Step 4: Start stream in launch endpoint**

Modify the `/{clientId}/launch` endpoint in `src/Beacon.Server/Api/ClientEndpoints.cs` to inject `IStreamingBackend streaming` and `IDisplayBackend displayBackend`, then replace the final success block with:

```csharp
sessions.Save(planResult.Plan);

StreamingStartResult streamResult = await streaming.StartAsync(planResult.Plan, cancellationToken);
if (!streamResult.Success || streamResult.Session is null)
{
    DisplayRestoreResult restore = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
    string restoreStatus = restore.Success
        ? "Physical primary restore requested after stream start failure."
        : $"Physical primary restore failed after stream start failure: {restore.Error}";

    return Results.Problem(
        $"{streamResult.Error} {restoreStatus}",
        statusCode: StatusCodes.Status503ServiceUnavailable);
}

return Results.Ok(new
{
    clientId,
    displayId = leaseResult.Lease.DisplayId,
    state = "streaming",
    stream = streamResult.Session
});
```

Add the required using:

```csharp
using Beacon.Core.Streaming;
```

- [x] **Step 5: Update old launch assertions**

Update existing launch tests in `ClientApiTests` from:

```csharp
Assert.Equal("started", root.GetProperty("state").GetString());
```

to:

```csharp
Assert.Equal("streaming", root.GetProperty("state").GetString());
```

- [x] **Step 6: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
```

Expected: all client API tests pass.

- [x] **Step 7: Commit**

```powershell
git add src/Beacon.Server tests/Beacon.Server.Tests
git commit -m "Start streams from launch endpoint"
git push
```

## Task 3: Stream Stop, Disconnect, And Status Endpoints

**Files:**
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Test: `tests/Beacon.Server.Tests/ClientApiTests.cs`

- [x] **Step 1: Write failing lifecycle tests**

Add these tests to `ClientApiTests`:

```csharp
[Fact]
public async Task StreamStatusAndStopAreIndependentFromDisplayCleanup()
{
    HttpClient client = factory.CreateClient();

    await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
    HttpResponseMessage statusBeforeStop = await client.GetAsync("/clients/z-fold-7/stream");
    HttpResponseMessage stop = await client.PostAsJsonAsync("/clients/z-fold-7/stream/stop", new { });
    HttpResponseMessage quit = await client.PostAsJsonAsync("/clients/z-fold-7/quit", new
    {
        clientActive = false,
        ownedProcessRunning = false,
        ownedWindowRemaining = false
    });

    Assert.Equal(HttpStatusCode.OK, statusBeforeStop.StatusCode);
    Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
    Assert.Equal(HttpStatusCode.OK, quit.StatusCode);

    using JsonDocument statusJson = await JsonDocument.ParseAsync(await statusBeforeStop.Content.ReadAsStreamAsync());
    using JsonDocument stopJson = await JsonDocument.ParseAsync(await stop.Content.ReadAsStreamAsync());
    using JsonDocument quitJson = await JsonDocument.ParseAsync(await quit.Content.ReadAsStreamAsync());

    Assert.Equal("running", statusJson.RootElement.GetProperty("stream").GetProperty("state").GetString());
    Assert.Equal("stopped", stopJson.RootElement.GetProperty("stream").GetProperty("state").GetString());
    Assert.True(quitJson.RootElement.GetProperty("displayRemoved").GetBoolean());
}

[Fact]
public async Task DisconnectStopsStreamAndRetainsDisplayLease()
{
    HttpClient client = factory.CreateClient();

    await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
    HttpResponseMessage disconnect = await client.PostAsJsonAsync("/clients/z-fold-7/disconnect", new { });

    Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
    using JsonDocument document = await JsonDocument.ParseAsync(await disconnect.Content.ReadAsStreamAsync());
    JsonElement root = document.RootElement;

    Assert.True(root.GetProperty("leaseRetained").GetBoolean());
    Assert.Equal("stopped", root.GetProperty("stream").GetProperty("state").GetString());
}
```

- [x] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "StreamStatusAndStopAreIndependentFromDisplayCleanup|DisconnectStopsStreamAndRetainsDisplayLease"
```

Expected: fail because `/stream`, `/stream/stop`, and disconnect stream stop behavior do not exist.

- [x] **Step 3: Add stream status endpoint**

Add this endpoint after launch in `ClientEndpoints`:

```csharp
clients.MapGet("/{clientId}/stream", async (
    string clientId,
    InMemorySessionStore sessions,
    IStreamingBackend streaming,
    CancellationToken cancellationToken) =>
{
    SessionPlan? plan = sessions.Get(clientId);
    if (plan is null)
    {
        return Results.NotFound(new { error = $"Client '{clientId}' has no session plan." });
    }

    StreamingSessionState? stream = await streaming.GetSessionAsync(plan.SessionId, cancellationToken);
    return stream is null
        ? Results.NotFound(new { error = $"Stream session '{plan.SessionId}' is not running." })
        : Results.Ok(new { clientId, stream });
});
```

- [x] **Step 4: Add stream stop endpoint**

Add this endpoint after stream status:

```csharp
clients.MapPost("/{clientId}/stream/stop", async (
    string clientId,
    InMemorySessionStore sessions,
    IStreamingBackend streaming,
    CancellationToken cancellationToken) =>
{
    SessionPlan? plan = sessions.Get(clientId);
    if (plan is null)
    {
        return Results.NotFound(new { error = $"Client '{clientId}' has no session plan." });
    }

    StreamingStopResult stop = await streaming.StopAsync(plan.SessionId, cancellationToken);
    return stop.Success && stop.Session is not null
        ? Results.Ok(new { clientId, stream = stop.Session })
        : Results.NotFound(new { error = stop.Error });
});
```

- [x] **Step 5: Stop stream on disconnect and quit**

Modify disconnect to inject `InMemorySessionStore sessions` and `IStreamingBackend streaming`, then return:

```csharp
await leases.DisconnectAsync(DisplayLease.CreateDisplayId(new ClientId(clientId)), cancellationToken);
SessionPlan? plan = sessions.Get(clientId);
StreamingSessionState? stream = null;
if (plan is not null)
{
    StreamingStopResult stop = await streaming.StopAsync(plan.SessionId, cancellationToken);
    stream = stop.Session;
}

return Results.Ok(new { clientId, leaseRetained = true, stream });
```

Modify quit to inject `InMemorySessionStore sessions` and `IStreamingBackend streaming`, then stop any known stream before `CleanupIfAllowedAsync`:

```csharp
SessionPlan? plan = sessions.Get(clientId);
StreamingSessionState? stream = null;
if (plan is not null)
{
    StreamingStopResult stop = await streaming.StopAsync(plan.SessionId, cancellationToken);
    stream = stop.Session;
}
```

Add `stream` to the quit response:

```csharp
return Results.Ok(new { clientId, cleanupEvaluated = true, displayRemoved = removed, stream });
```

- [x] **Step 6: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
```

Expected: all client API tests pass.

- [x] **Step 7: Commit**

```powershell
git add src/Beacon.Server tests/Beacon.Server.Tests
git commit -m "Add stream lifecycle endpoints"
git push
```

## Task 4: Admin Snapshot And WPF Stream State

**Files:**
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`
- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitModels.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs`
- Modify: `src/Beacon.Cockpit/MainWindow.xaml`
- Modify: `tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs`
- Modify: `tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs`

- [x] **Step 1: Write failing admin snapshot stream test**

Modify `SnapshotReturnsClientsGamesAndSessions` in `AdminApiTests` to launch a game before snapshot:

```csharp
await client.PostAsJsonAsync("/clients/z-fold-7/launch", new { gameId = "steam-shortcut:3767414131" });
HttpResponseMessage response = await client.GetAsync("/admin/snapshot");
```

Add:

```csharp
Assert.Equal("running", root.GetProperty("streams")[0].GetProperty("state").GetString());
Assert.Equal("client-z-fold-7", root.GetProperty("streams")[0].GetProperty("displayId").GetString());
```

- [x] **Step 2: Write failing cockpit model tests**

Update the JSON in `CockpitApiClientTests.LoadsSnapshotFromServer`:

```json
"streams": [{ "sessionId": "z-fold-7-steam-shortcut:3767414131", "clientId": "z-fold-7", "appId": "steam-shortcut:3767414131", "displayId": "client-z-fold-7", "codec": "av1", "fps": 120, "initialBitrateMbps": 65, "transport": "lan-direct", "state": "running", "error": null }]
```

Add assertions:

```csharp
Assert.Single(snapshot.Streams);
Assert.Equal("running", snapshot.Streams[0].State);
Assert.Equal("client-z-fold-7", snapshot.Streams[0].DisplayId);
```

Update `CockpitShellViewModelTests.RefreshPopulatesDashboardState` to pass a stream:

```csharp
[new CockpitStreamSummary(
    "z-fold-7-steam-shortcut:3767414131",
    "z-fold-7",
    "steam-shortcut:3767414131",
    "client-z-fold-7",
    "av1",
    120,
    65,
    "lan-direct",
    "running",
    null)]
```

Add assertions:

```csharp
Assert.Equal(1, viewModel.StreamCount);
Assert.Contains("z-fold-7 steam-shortcut:3767414131 running av1 120fps", viewModel.Streams);
```

- [x] **Step 3: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter AdminApiTests
dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj
```

Expected: fail because snapshots and cockpit models do not include streams.

- [x] **Step 4: Add streams to admin endpoint**

Modify `src/Beacon.Server/Api/AdminEndpoints.cs` to inject `IStreamingBackend streaming` into `/admin/snapshot`, and add:

```csharp
streams = streaming.GetSessions(),
```

Add:

```csharp
using Beacon.Core.Streaming;
```

- [x] **Step 5: Add cockpit stream DTOs**

Modify `CockpitModels.cs`:

```csharp
public sealed record CockpitSnapshot(
    IReadOnlyList<CockpitClientSummary> Clients,
    IReadOnlyList<CockpitSessionSummary> Sessions,
    IReadOnlyList<CockpitStreamSummary> Streams,
    CockpitGameSummary Games);

public sealed record CockpitStreamSummary(
    string SessionId,
    string ClientId,
    string AppId,
    string DisplayId,
    string Codec,
    int Fps,
    int InitialBitrateMbps,
    string Transport,
    string State,
    string? Error);
```

Update empty snapshot construction in `CockpitApiClient`:

```csharp
return snapshot ?? new CockpitSnapshot([], [], [], new CockpitGameSummary(0, []));
```

- [x] **Step 6: Add ViewModel stream projection**

Add properties to `CockpitShellViewModel`:

```csharp
private int streamCount;

public int StreamCount
{
    get => streamCount;
    private set => SetProperty(ref streamCount, value);
}

public ObservableCollection<string> Streams { get; } = [];
```

In `RefreshAsync`, add:

```csharp
Replace(Streams, snapshot.Streams.Select(stream =>
    $"{stream.ClientId} {stream.AppId} {stream.State} {stream.Codec} {stream.Fps}fps"));
StreamCount = snapshot.Streams.Count;
```

- [x] **Step 7: Add stream UI**

Modify `MainWindow.xaml`:

- Add a left summary block:

```xml
<TextBlock Text="Streams" Style="{StaticResource LabelText}" />
<TextBlock Text="{Binding StreamCount}" Style="{StaticResource MetricText}" />
```

- Add a `Streams` tab:

```xml
<TabItem Header="Streams">
    <Grid Margin="10">
        <ListBox ItemsSource="{Binding Streams}" />
    </Grid>
</TabItem>
```

- [x] **Step 8: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter AdminApiTests
dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj
dotnet build src/Beacon.Cockpit/Beacon.Cockpit.csproj -warnaserror
```

Expected: admin, cockpit tests, and WPF build pass.

- [x] **Step 9: Commit**

```powershell
git add src/Beacon.Server tests/Beacon.Server.Tests src/Beacon.Cockpit tests/Beacon.Cockpit.Tests
git commit -m "Expose stream state in cockpit"
git push
```

## Task 5: External Process Streaming Backend Boundary

**Files:**
- Create: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`
- Test: `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`
- Modify: `docs/extraction-map.md`

- [x] **Step 1: Write failing command-building tests**

Create `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`:

```csharp
using Beacon.Core.Clients;
using Beacon.Core.Displays;
using Beacon.Core.Sessions;
using Beacon.Platform.Windows.Streaming;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class ExternalProcessStreamingBackendTests
{
    [Fact]
    public void CreateStartInfoPassesSessionPlanAsArgumentsAndEnvironment()
    {
        SessionPlan plan = CreatePlan();

        ExternalStreamingCommand command = ExternalProcessStreamingBackend.CreateStartCommand(
            "C:\\Tools\\sunshine-wrapper.exe",
            plan);

        Assert.Equal("C:\\Tools\\sunshine-wrapper.exe", command.FileName);
        Assert.Contains("--session", command.Arguments);
        Assert.Contains("z-fold-7-steam-shortcut:3767414131", command.Arguments);
        Assert.Equal("client-z-fold-7", command.Environment["BEACON_DISPLAY_ID"]);
        Assert.Equal("av1", command.Environment["BEACON_STREAM_CODEC"]);
        Assert.Equal("120", command.Environment["BEACON_STREAM_FPS"]);
        Assert.Equal("65", command.Environment["BEACON_STREAM_BITRATE_MBPS"]);
    }

    private static SessionPlan CreatePlan() =>
        new(
            "z-fold-7-steam-shortcut:3767414131",
            new ClientId("z-fold-7"),
            "steam-shortcut:3767414131",
            new PlannedDisplay("client-z-fold-7", 2560, 1600, 120, "virtual-primary", HdrPreference.Prefer, false, "sdr", "HDR unavailable."),
            new PlannedStream("av1", 120, 65, "lan-direct", "adaptive"));
}
```

- [x] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackendTests
```

Expected: fail because the external streaming backend does not exist.

- [x] **Step 3: Implement command boundary**

Create `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`:

```csharp
using Beacon.Core.Sessions;

namespace Beacon.Platform.Windows.Streaming;

public sealed record ExternalStreamingCommand(
    string FileName,
    string Arguments,
    IReadOnlyDictionary<string, string> Environment);

public sealed class ExternalProcessStreamingBackend
{
    public static ExternalStreamingCommand CreateStartCommand(string executablePath, SessionPlan plan)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BEACON_SESSION_ID"] = plan.SessionId,
            ["BEACON_CLIENT_ID"] = plan.ClientId.Value,
            ["BEACON_APP_ID"] = plan.AppId,
            ["BEACON_DISPLAY_ID"] = plan.Display.DisplayId,
            ["BEACON_STREAM_CODEC"] = plan.Stream.Codec,
            ["BEACON_STREAM_FPS"] = plan.Stream.Fps.ToString(CultureInfo.InvariantCulture),
            ["BEACON_STREAM_BITRATE_MBPS"] = plan.Stream.InitialBitrateMbps.ToString(CultureInfo.InvariantCulture),
            ["BEACON_STREAM_TRANSPORT"] = plan.Stream.Transport
        };

        string arguments = $"--session \"{plan.SessionId}\" --display \"{plan.Display.DisplayId}\"";
        return new ExternalStreamingCommand(executablePath, arguments, environment);
    }
}
```

Add:

```csharp
using System.Globalization;
```

- [x] **Step 4: Update extraction map**

Modify the Sunshine row in `docs/extraction-map.md` to:

```markdown
| Sunshine | Streaming protocol, capture, encode, audio, input reference; Milestone 5 external process boundary only | No | `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs` passes Beacon plans to a wrapper without copying Sunshine source |
```

- [x] **Step 5: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackendTests
```

Expected: command-boundary test passes.

- [x] **Step 6: Commit**

```powershell
git add src/Beacon.Platform.Windows/Streaming tests/Beacon.Platform.Windows.Tests/Streaming docs/extraction-map.md
git commit -m "Add external streaming process boundary"
git push
```

## Task 6: Client Lab Streaming State

**Files:**
- Modify: `src/Beacon.ClientLab/src/clientLab.ts`
- Modify: `src/Beacon.ClientLab/src/main.ts`
- Modify: `src/Beacon.ClientLab/src/clientLab.test.ts`
- Modify: `tests/Beacon.ClientLab.Playwright/tests/client-lab.spec.ts`

- [x] **Step 1: Write failing Client Lab tests**

Update the launch response fixture in `src/Beacon.ClientLab/src/clientLab.test.ts` to:

```typescript
launch: {
  clientId: 'z-fold-7',
  displayId: 'client-z-fold-7',
  state: 'streaming',
  stream: {
    sessionId: 'z-fold-7-steam-shortcut:3767414131',
    clientId: 'z-fold-7',
    appId: 'steam-shortcut:3767414131',
    displayId: 'client-z-fold-7',
    codec: 'av1',
    fps: 120,
    initialBitrateMbps: 65,
    transport: 'lan-direct',
    state: 'running',
    error: null
  }
}
```

Add:

```typescript
expect(result.launch.stream?.state).toBe('running');
expect(result.launch.stream?.fps).toBe(120);
```

Update the Playwright assertion from `started` to:

```typescript
await expect(page.locator('#log')).toContainText('streaming');
await expect(page.locator('#log')).toContainText('running av1 120fps');
```

- [x] **Step 2: Verify red**

Run:

```powershell
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Expected: fail because Client Lab does not model the `stream` response yet.

- [x] **Step 3: Add stream response types**

Modify `src/Beacon.ClientLab/src/clientLab.ts`:

```typescript
export interface StreamState {
  sessionId: string;
  clientId: string;
  appId: string;
  displayId: string;
  codec: string;
  fps: number;
  initialBitrateMbps: number;
  transport: string;
  state: string;
  error: string | null;
}

export interface LaunchResponse {
  clientId: string;
  displayId: string;
  state: string;
  stream: StreamState | null;
}
```

- [x] **Step 4: Render stream state**

Modify launch handling in `src/Beacon.ClientLab/src/main.ts`:

```typescript
appendLog(`${launch.state} ${launch.displayId}`);
if (launch.stream) {
  appendLog(`${launch.stream.state} ${launch.stream.codec} ${launch.stream.fps}fps`);
}
```

- [x] **Step 5: Verify green**

Run:

```powershell
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright lint
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Expected: TypeScript, Vitest, and Playwright pass.

- [x] **Step 6: Commit**

```powershell
git add src/Beacon.ClientLab tests/Beacon.ClientLab.Playwright
git commit -m "Show stream state in client lab"
git push
```

## Task 7: Docs, Validation, Refactor, And PR

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-07-beacon-stream-milestone-5-streaming-backend.md`

- [x] **Step 1: Update docs**

Add to `README.md`:

````markdown
Streaming backend checks:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter FakeStreamingBackendTests
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
```

Milestone 5 currently uses `FakeStreamingBackend` for deterministic no-phone validation and `ExternalProcessStreamingBackend` as the Windows boundary for future Sunshine-compatible process integration. No Sunshine source is copied by this milestone.
````

- [x] **Step 2: Static validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
```

Expected: both commands pass.

- [x] **Step 3: Dynamic validation**

Run:

```powershell
dotnet test Beacon.slnx
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright lint
pnpm --dir tests/Beacon.ClientLab.Playwright test
dotnet run --project src/Beacon.GameProbe -- scan --json
```

Expected: all tests pass. GameProbe may still report the local stale `G:\SteamLibrary\steamapps` diagnostic.

- [x] **Step 4: Boundary audit**

Run:

```powershell
rg "LizardByte|Sunshine/src|nvhttp|rtsp|moonlight" src tests -n
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
```

Expected:

- First command only matches documentation strings or the `ExternalProcessStreamingBackend` boundary; no copied Sunshine source appears under `src`.
- Second command has no new cancellation timeout patterns.

- [x] **Step 5: Commit docs and validation record**

```powershell
git add README.md docs/superpowers/plans/2026-07-07-beacon-stream-milestone-5-streaming-backend.md
git commit -m "Document streaming backend milestone"
git push
```

- [ ] **Step 6: Open PR and wait for CI**

```powershell
gh pr create --draft --base main --head codex/milestone-5-streaming-backend --title "Implement streaming backend boundary" --body "Milestone 5 streaming backend boundary implementation."
gh pr checks --watch
gh pr ready
```

Expected: CI passes before the PR is marked ready.

## Requirement Coverage

- `REQ-CTRL-008`: launch uses an effective session plan before display and stream start.
- `REQ-CTRL-009`: client consumes stream plan; server starts backend from the plan.
- `REQ-DISP-005`: disconnect stops stream without tearing down display.
- `REQ-DISP-006`: stream stop and display cleanup are separate operations.
- `REQ-DISP-011`: stream launch does not silently fall back to physical display when lease fails.
- `REQ-SESS-006`: disconnect/reconnect keeps display lease coherent while stream state changes independently.
- `REQ-SESS-008`: streaming backend start failure is explicit and triggers physical restore attempt.
- `REQ-MODE-005`: launch response carries selected stream and display state.
- `REQ-HDR-010`: stream backend receives the plan that includes HDR mode decisions.
- `REQ-NET-003`: codec, FPS, bitrate, transport, and congestion policy are passed to backend.
- `REQ-NET-005`: 120 FPS is explicit in stream session state.
- `REQ-REC-008`: stream backend errors are surfaced through API responses and admin/cockpit state.
- `REQ-TEST-007`: streaming backend is interface-backed and fake-testable.
- `REQ-TEST-010`: real phone remains final confirmation, not a prerequisite for this milestone.

## Plan Self-Review

- Scope check: this is Milestone 5 only. Android APK, real Moonlight client testing, and copied Sunshine protocol code are out of scope.
- Placeholder scan: no task contains unresolved implementation-marker language.
- Type consistency: all tasks use `IStreamingBackend`, `StreamingSessionState`, `StreamingStartResult`, and `StreamingStopResult`.
- Boundary check: Sunshine is reference-only unless a later task explicitly updates `docs/extraction-map.md` before copied/adapted source lands.
- Timeout check: no implementation step uses cancellation timeouts. Stream stop is an explicit lifecycle command, not a timer.
