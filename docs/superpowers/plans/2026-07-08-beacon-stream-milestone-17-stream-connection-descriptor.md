# Stream Connection Descriptor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a server-owned stream connection descriptor so clients receive a concrete, typed connection contract after launch instead of only seeing that a backend state is `running`.

**Architecture:** Keep streaming policy and connection metadata in `Beacon.Core.Streaming`. Backends may expose a descriptor when they know the connectable URI/endpoints; fake mode always exposes a deterministic descriptor for no-phone tests, while external-process mode exposes configured descriptor data without copying Sunshine or Moonlight code. Server, WPF cockpit, ClientLab, and Android raw responses should carry the descriptor without making the client reinterpret display or stream policy.

**Tech Stack:** .NET 10, ASP.NET minimal APIs, xUnit, WPF, TypeScript/Vitest/Playwright, Java Android JVM tests where needed.

---

## Requirements Covered

- `REQ-CTRL-009`: the client consumes server-computed stream connection data instead of inventing local stream policy.
- `REQ-NET-003`: codec, FPS, bitrate, transport, and connection metadata are selected by the server/backend boundary.
- `REQ-MODE-005`: the launch response explains the selected stream/display state and gives the client the connection target.
- `REQ-REC-008`: diagnostics and cockpit state expose streaming backend details.
- `REQ-TEST-002`: Client Lab can simulate the remote client seeing the connection descriptor.
- `REQ-TEST-007`: streaming remains interface-backed and fakeable.
- `REQ-TEST-010`: real phone testing remains final confirmation; this milestone stays phone-free.

## File Map

- Modify: `src/Beacon.Core/Streaming/StreamingSessionState.cs`
  - Add `StreamingConnectionDescriptor` and `StreamingEndpointDescriptor`.
  - Add nullable `Connection` to `StreamingSessionState`.
- Modify: `src/Beacon.Core/Streaming/FakeStreamingBackend.cs`
  - Emit a deterministic fake connection descriptor for each started session.
- Modify: `tests/Beacon.Core.Tests/Streaming/FakeStreamingBackendTests.cs`
  - Assert fake stream sessions include the connection descriptor.
- Modify: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`
  - Extend external-process options with optional connection protocol, launch URI, and endpoint URI map.
  - Pass connection fields as environment variables to the wrapper.
  - Include a descriptor in session state when configured.
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
  - Bind optional external connection config/environment variables.
- Modify: `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`
  - Assert command environment and session state carry configured connection metadata.
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`
  - Assert external connection settings are resolved from configuration/environment.
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
  - Assert launch and stream status responses include the fake descriptor.
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitModels.cs`
  - Add stream connection DTOs.
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs`
  - Render stream connection URI in the stream list when present.
- Modify: `tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs`
  - Parse connection data from admin snapshot JSON.
- Modify: `tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs`
  - Assert stream list includes connection URI.
- Modify: `src/Beacon.ClientLab/src/clientLab.ts`
  - Add stream connection types.
- Modify: `src/Beacon.ClientLab/src/main.ts`
  - Render stream connection URI.
- Modify: `src/Beacon.ClientLab/src/clientLab.test.ts`
  - Assert simulated launch exposes connection metadata.
- Modify: `tests/Beacon.ClientLab.Playwright/tests/client-lab.spec.ts`
  - Assert browser flow shows the connection URI.
- Modify: `README.md`
  - Document the stream connection descriptor and external-process config.
- Modify: `docs/extraction-map.md`
  - Keep Sunshine/Moonlight reference-only and note this milestone adds Beacon-owned connection metadata only.

## Connection Shape

Add these records in `src/Beacon.Core/Streaming/StreamingSessionState.cs`:

```csharp
public sealed record StreamingConnectionDescriptor(
    string Protocol,
    string? LaunchUri,
    IReadOnlyList<StreamingEndpointDescriptor> Endpoints,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record StreamingEndpointDescriptor(
    string Role,
    string Uri);
```

Extend `StreamingSessionState`:

```csharp
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
    string? Error,
    StreamingConnectionDescriptor? Connection);
```

## Task 1: Core Fake Descriptor

**Files:**
- Modify: `src/Beacon.Core/Streaming/StreamingSessionState.cs`
- Modify: `src/Beacon.Core/Streaming/FakeStreamingBackend.cs`
- Test: `tests/Beacon.Core.Tests/Streaming/FakeStreamingBackendTests.cs`

- [ ] **Step 1: Write failing fake backend descriptor test**

Add to `StartCreatesRunningSessionFromPlan`:

```csharp
Assert.NotNull(session.Connection);
Assert.Equal("beacon-fake", session.Connection.Protocol);
Assert.Equal("beacon-fake://stream/z-fold-7-steam-shortcut:3767414131", session.Connection.LaunchUri);
StreamingEndpointDescriptor endpoint = Assert.Single(session.Connection.Endpoints);
Assert.Equal("control", endpoint.Role);
Assert.Equal("beacon-fake://stream/z-fold-7-steam-shortcut:3767414131", endpoint.Uri);
Assert.Equal("client-z-fold-7", session.Connection.Metadata["displayId"]);
```

- [ ] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter FakeStreamingBackendTests
```

Expected: compile failure because `StreamingSessionState.Connection` does not exist.

- [ ] **Step 3: Add descriptor records and session property**

Replace `src/Beacon.Core/Streaming/StreamingSessionState.cs` with:

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
    string? Error,
    StreamingConnectionDescriptor? Connection);

public sealed record StreamingConnectionDescriptor(
    string Protocol,
    string? LaunchUri,
    IReadOnlyList<StreamingEndpointDescriptor> Endpoints,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record StreamingEndpointDescriptor(
    string Role,
    string Uri);
```

- [ ] **Step 4: Populate fake descriptor**

In `FakeStreamingBackend.StartAsync`, build the descriptor before constructing `StreamingSessionState`:

```csharp
string launchUri = $"beacon-fake://stream/{plan.SessionId}";
var connection = new StreamingConnectionDescriptor(
    "beacon-fake",
    launchUri,
    [new StreamingEndpointDescriptor("control", launchUri)],
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["displayId"] = plan.Display.DisplayId,
        ["transport"] = plan.Stream.Transport
    });
```

Pass `connection` as the final `StreamingSessionState` constructor argument.

In `StopAsync`, keep the existing connection by using:

```csharp
StreamingSessionState stopped = session with { State = "stopped" };
```

- [ ] **Step 5: Update other constructor calls**

Update every `new StreamingSessionState(...)` call to pass either a descriptor or `Connection: null`.

Run:

```powershell
rg "new StreamingSessionState" src tests -n
```

Expected: each call has the new `Connection` argument.

- [ ] **Step 6: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter FakeStreamingBackendTests
```

Expected: all fake backend tests pass.

## Task 2: External Process Descriptor Configuration

**Files:**
- Modify: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Test: `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`
- Test: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`

- [ ] **Step 1: Write failing external backend tests**

Add to `ExternalProcessStreamingBackendTests`:

```csharp
[Fact]
public async Task StartIncludesConfiguredConnectionDescriptor()
{
    var runner = new FakeExternalStreamingProcessRunner();
    runner.ExistingFiles.Add("C:\\Tools\\sunshine-wrapper.exe");
    var options = new ExternalProcessStreamingOptions(
        "C:\\Tools\\sunshine-wrapper.exe",
        "gamestream",
        "moonlight://beacon/z-fold-7-steam-shortcut:3767414131",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rtsp"] = "rtsp://127.0.0.1:48010/beacon",
            ["input"] = "udp://127.0.0.1:48000"
        });
    var backend = new ExternalProcessStreamingBackend(options, runner);

    StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

    StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
    Assert.NotNull(session.Connection);
    Assert.Equal("gamestream", session.Connection.Protocol);
    Assert.Equal("moonlight://beacon/z-fold-7-steam-shortcut:3767414131", session.Connection.LaunchUri);
    Assert.Contains(session.Connection.Endpoints, endpoint => endpoint.Role == "rtsp" && endpoint.Uri == "rtsp://127.0.0.1:48010/beacon");
    ExternalStreamingCommand command = Assert.Single(runner.StartedCommands);
    Assert.Equal("gamestream", command.Environment["BEACON_CONNECTION_PROTOCOL"]);
    Assert.Equal("moonlight://beacon/z-fold-7-steam-shortcut:3767414131", command.Environment["BEACON_CONNECTION_LAUNCH_URI"]);
    Assert.Equal("input=udp://127.0.0.1:48000;rtsp=rtsp://127.0.0.1:48010/beacon", command.Environment["BEACON_CONNECTION_ENDPOINTS"]);
}
```

Add to `BeaconServiceRegistrationTests`:

```csharp
[Fact]
public void ExternalProcessConnectionOptionsUseConfiguration()
{
    IConfiguration configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [BeaconServiceRegistration.StreamingBackendConfigurationKey] = "external-process",
            [BeaconServiceRegistration.ExternalStreamingExecutableConfigurationKey] = "C:\\Tools\\sunshine-wrapper.exe",
            ["Beacon:Streaming:ExternalProcess:Connection:Protocol"] = "gamestream",
            ["Beacon:Streaming:ExternalProcess:Connection:LaunchUri"] = "moonlight://beacon/session",
            ["Beacon:Streaming:ExternalProcess:Connection:Endpoints:rtsp"] = "rtsp://127.0.0.1:48010/beacon"
        })
        .Build();

    using ServiceProvider provider = new ServiceCollection()
        .AddBeaconServices(configuration)
        .BuildServiceProvider();

    ExternalProcessStreamingOptions options = provider.GetRequiredService<ExternalProcessStreamingOptions>();
    Assert.Equal("gamestream", options.ConnectionProtocol);
    Assert.Equal("moonlight://beacon/session", options.ConnectionLaunchUri);
    Assert.Equal("rtsp://127.0.0.1:48010/beacon", options.ConnectionEndpoints["rtsp"]);
}
```

- [ ] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackendTests
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ExternalProcessConnectionOptionsUseConfiguration
```

Expected: compile failures because the new options do not exist.

- [ ] **Step 3: Extend external options**

Change the options record in `ExternalProcessStreamingBackend.cs`:

```csharp
public sealed record ExternalProcessStreamingOptions(
    string? ExecutablePath,
    string? ConnectionProtocol = null,
    string? ConnectionLaunchUri = null,
    IReadOnlyDictionary<string, string>? ConnectionEndpoints = null);
```

Add helper methods:

```csharp
private static StreamingConnectionDescriptor? CreateConnectionDescriptor(ExternalProcessStreamingOptions options)
{
    if (string.IsNullOrWhiteSpace(options.ConnectionProtocol)
        && string.IsNullOrWhiteSpace(options.ConnectionLaunchUri)
        && (options.ConnectionEndpoints is null || options.ConnectionEndpoints.Count == 0))
    {
        return null;
    }

    string protocol = string.IsNullOrWhiteSpace(options.ConnectionProtocol)
        ? "external-process"
        : options.ConnectionProtocol.Trim();
    IReadOnlyList<StreamingEndpointDescriptor> endpoints = (options.ConnectionEndpoints ?? new Dictionary<string, string>())
        .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .Select(pair => new StreamingEndpointDescriptor(pair.Key.Trim(), pair.Value.Trim()))
        .ToArray();

    return new StreamingConnectionDescriptor(
        protocol,
        string.IsNullOrWhiteSpace(options.ConnectionLaunchUri) ? null : options.ConnectionLaunchUri.Trim(),
        endpoints,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
```

Pass `CreateConnectionDescriptor(options)` as the final `StreamingSessionState` argument.

- [ ] **Step 4: Pass descriptor config to wrapper environment**

In `CreateStartCommand`, accept an optional `ExternalProcessStreamingOptions` parameter:

```csharp
public static ExternalStreamingCommand CreateStartCommand(
    string executablePath,
    SessionPlan plan,
    ExternalProcessStreamingOptions? options = null)
```

After the existing `BEACON_STREAM_*` values, add:

```csharp
if (!string.IsNullOrWhiteSpace(options?.ConnectionProtocol))
{
    environment["BEACON_CONNECTION_PROTOCOL"] = options.ConnectionProtocol.Trim();
}

if (!string.IsNullOrWhiteSpace(options?.ConnectionLaunchUri))
{
    environment["BEACON_CONNECTION_LAUNCH_URI"] = options.ConnectionLaunchUri.Trim();
}

if (options?.ConnectionEndpoints is { Count: > 0 })
{
    environment["BEACON_CONNECTION_ENDPOINTS"] = string.Join(
        ';',
        options.ConnectionEndpoints
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key.Trim()}={pair.Value.Trim()}"));
}
```

Change `StartAsync` to call:

```csharp
ExternalStreamingCommand command = CreateStartCommand(options.ExecutablePath!, plan, options);
```

- [ ] **Step 5: Bind service configuration**

In `BeaconServiceRegistration`, add constants:

```csharp
public const string ExternalStreamingConnectionProtocolConfigurationKey = "Beacon:Streaming:ExternalProcess:Connection:Protocol";
public const string ExternalStreamingConnectionLaunchUriConfigurationKey = "Beacon:Streaming:ExternalProcess:Connection:LaunchUri";
public const string ExternalStreamingConnectionProtocolEnvironmentVariable = "BEACON_EXTERNAL_STREAMING_CONNECTION_PROTOCOL";
public const string ExternalStreamingConnectionLaunchUriEnvironmentVariable = "BEACON_EXTERNAL_STREAMING_CONNECTION_LAUNCH_URI";
```

Add a resolver:

```csharp
private static ExternalProcessStreamingOptions CreateExternalProcessStreamingOptions(
    IConfiguration configuration,
    string? environmentExternalStreamingExecutable)
{
    string? protocol = Environment.GetEnvironmentVariable(ExternalStreamingConnectionProtocolEnvironmentVariable)
        ?? configuration[ExternalStreamingConnectionProtocolConfigurationKey];
    string? launchUri = Environment.GetEnvironmentVariable(ExternalStreamingConnectionLaunchUriEnvironmentVariable)
        ?? configuration[ExternalStreamingConnectionLaunchUriConfigurationKey];
    Dictionary<string, string> endpoints = configuration
        .GetSection("Beacon:Streaming:ExternalProcess:Connection:Endpoints")
        .GetChildren()
        .Where(child => !string.IsNullOrWhiteSpace(child.Key) && !string.IsNullOrWhiteSpace(child.Value))
        .ToDictionary(child => child.Key, child => child.Value!, StringComparer.OrdinalIgnoreCase);

    return new ExternalProcessStreamingOptions(
        ResolveExternalStreamingExecutable(configuration, environmentExternalStreamingExecutable),
        protocol,
        launchUri,
        endpoints);
}
```

In external backend registration, replace `new ExternalProcessStreamingOptions(...)` with `CreateExternalProcessStreamingOptions(...)`.

- [ ] **Step 6: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackendTests
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter BeaconServiceRegistrationTests
```

Expected: all tests pass.

## Task 3: Server, Cockpit, And Client Lab Visibility

**Files:**
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitModels.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs`
- Modify: `tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs`
- Modify: `tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs`
- Modify: `src/Beacon.ClientLab/src/clientLab.ts`
- Modify: `src/Beacon.ClientLab/src/main.ts`
- Modify: `src/Beacon.ClientLab/src/clientLab.test.ts`
- Modify: `tests/Beacon.ClientLab.Playwright/tests/client-lab.spec.ts`

- [ ] **Step 1: Write failing server response assertions**

In `ClientApiTests.LaunchStartsStreamingBackendWithSessionPlan`, add:

```csharp
JsonElement connection = root.GetProperty("stream").GetProperty("connection");
Assert.Equal("beacon-fake", connection.GetProperty("protocol").GetString());
Assert.Equal("beacon-fake://stream/z-fold-7-steam-shortcut:3767414131", connection.GetProperty("launchUri").GetString());
Assert.Equal("control", connection.GetProperty("endpoints")[0].GetProperty("role").GetString());
```

In `StreamStatusAndStopAreIndependentFromDisplayCleanup`, add the same assertion against `statusJson.RootElement.GetProperty("stream").GetProperty("connection")`.

- [ ] **Step 2: Write failing cockpit model assertions**

Add records to `CockpitModels.cs`:

```csharp
public sealed record CockpitStreamConnection(
    string Protocol,
    string? LaunchUri,
    IReadOnlyList<CockpitStreamEndpoint> Endpoints,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CockpitStreamEndpoint(string Role, string Uri);
```

Add a `CockpitStreamConnection? Connection` constructor argument to `CockpitStreamSummary`.

Update `CockpitApiClientTests` JSON stream fixture with:

```json
"connection": {
  "protocol": "beacon-fake",
  "launchUri": "beacon-fake://stream/z-fold-7-steam-shortcut:3767414131",
  "endpoints": [{ "role": "control", "uri": "beacon-fake://stream/z-fold-7-steam-shortcut:3767414131" }],
  "metadata": { "displayId": "client-z-fold-7" }
}
```

Add assertions:

```csharp
Assert.Equal("beacon-fake", snapshot.Streams[0].Connection?.Protocol);
Assert.Equal("beacon-fake://stream/z-fold-7-steam-shortcut:3767414131", snapshot.Streams[0].Connection?.LaunchUri);
```

Update `CockpitShellViewModelTests` to pass the same `CockpitStreamConnection` and assert:

```csharp
Assert.Contains("beacon-fake://stream/z-fold-7-steam-shortcut:3767414131", viewModel.Streams);
```

- [ ] **Step 3: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj
```

Expected: failures until stream/cockpit DTOs expose the connection property.

- [ ] **Step 4: Implement cockpit projection**

In `CockpitShellViewModel.RefreshAsync`, render:

```csharp
Replace(Streams, snapshot.Streams.Select(stream =>
{
    string connection = string.IsNullOrWhiteSpace(stream.Connection?.LaunchUri)
        ? "no connection URI"
        : stream.Connection.LaunchUri;
    return $"{stream.ClientId} {stream.AppId} {stream.State} {stream.Codec} {stream.Fps}fps {connection}";
}));
```

- [ ] **Step 5: Add Client Lab types and assertions**

In `src/Beacon.ClientLab/src/clientLab.ts`, add:

```typescript
export interface StreamConnectionEndpoint {
  role: string;
  uri: string;
}

export interface StreamConnection {
  protocol: string;
  launchUri: string | null;
  endpoints: StreamConnectionEndpoint[];
  metadata: Record<string, string>;
}
```

Add to `StreamState`:

```typescript
connection: StreamConnection | null;
```

In `src/Beacon.ClientLab/src/main.ts`, after rendering stream state:

```typescript
if (launch.stream?.connection?.launchUri) {
  appendLog(launch.stream.connection.launchUri);
}
```

In `src/Beacon.ClientLab/src/clientLab.test.ts`, add the fake connection object to the launch fixture and assert:

```typescript
expect(result.launch.stream?.connection?.protocol).toBe('beacon-fake');
expect(result.launch.stream?.connection?.launchUri).toBe('beacon-fake://stream/z-fold-7-steam-shortcut:3767414131');
```

In the Playwright test, add:

```typescript
await expect(page.locator('#log')).toContainText('beacon-fake://stream/z-fold-7-steam-shortcut:3767414131');
```

- [ ] **Step 6: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright lint
pnpm --dir tests\Beacon.ClientLab.Playwright test
```

Expected: all commands pass.

## Task 4: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-17-stream-connection-descriptor.md`

- [ ] **Step 1: Update docs**

Add to `README.md` under streaming backend notes:

```markdown
Milestone 17 adds a stream connection descriptor to each running stream state. Fake mode returns a deterministic `beacon-fake://...` URI for no-phone testing. External-process mode can expose a configured connection protocol, launch URI, and endpoint map through `Beacon:Streaming:ExternalProcess:Connection:*` or `BEACON_EXTERNAL_STREAMING_CONNECTION_*` environment variables.
```

Update the Sunshine row in `docs/extraction-map.md` to mention that Milestone 17 still uses Beacon-owned descriptor metadata only and copies no streaming protocol source.

- [ ] **Step 2: Static validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
```

Expected: both commands pass.

- [ ] **Step 3: Dynamic validation**

Run:

```powershell
dotnet test Beacon.slnx
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright lint
pnpm --dir tests\Beacon.ClientLab.Playwright test
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: all commands pass; Gradle 9 deprecation warning is acceptable if the build exits successfully.

- [ ] **Step 4: Boundary audit**

Run:

```powershell
rg "LizardByte|Sunshine/src|nvhttp|rtsp|moonlight" src tests -n
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
```

Expected: no copied upstream source appears under `src`; no timeout/cancellation helper is introduced.

- [ ] **Step 5: Commit and sync**

Run:

```powershell
git add README.md docs/extraction-map.md docs/superpowers/plans/2026-07-08-beacon-stream-milestone-17-stream-connection-descriptor.md src tests
git commit -m "Add stream connection descriptors"
git push -u origin codex/milestone-17-stream-connection-descriptor
gh pr create --draft --base main --head codex/milestone-17-stream-connection-descriptor --title "Add stream connection descriptors" --body "Milestone 17 stream connection descriptor implementation."
gh pr checks <pr-number> --watch
gh pr ready <pr-number>
gh pr merge <pr-number> --merge --delete-branch
```

Expected: PR checks pass, PR merges, and local `main` is clean.

## Non-Goals

- No native video decoder.
- No Android input forwarding.
- No copied Sunshine, Moonlight, Apollo, Vibeshine, or Vibepollo source.
- No new streaming protocol implementation.
- No global settings edited from the APK.
