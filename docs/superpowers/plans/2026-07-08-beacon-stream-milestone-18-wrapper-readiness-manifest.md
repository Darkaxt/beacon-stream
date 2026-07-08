# Wrapper Readiness Manifest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an optional manifest-based readiness contract for external streaming wrappers so Beacon can reject unsupported session plans before display creation, game launch, or wrapper startup.

**Architecture:** Keep the streaming wrapper boundary external-process based. The wrapper advertises capabilities through a JSON manifest file that Beacon reads during streaming preflight; Beacon does not execute a process just to ask for readiness, avoiding hangs and timeout-driven cancellation. Explicit connection descriptor settings remain supported and override manifest connection fields when both are configured.

**Tech Stack:** .NET 10, `System.Text.Json`, ASP.NET service registration, xUnit, existing ClientLab/Android validation.

---

## Primary Reference Notes

- Sunshine is a self-hosted Moonlight host with hardware/software encoding support, a web UI, and client pairing. Beacon should treat Sunshine-compatible behavior as an external host/wrapper boundary, not merge Sunshine policy into the server.
- Sunshine supports a configuration file path as its first startup argument and keeps `apps.json` near the configuration path by default. Beacon can pass a configured wrapper executable and manifest path instead of editing Sunshine config directly.
- Sunshine `global_prep_cmd` runs before/after applications and aborts app start if a prep command fails. Beacon must not rely on prep commands for display topology because Beacon owns display and stream preflight before launch.
- Moonlight setup expects pairing with a compatible host such as Sunshine. Beacon's wrapper manifest is a stepping stone toward that host/client path, not a replacement for real pairing or protocol work.

Reference links:

- `https://github.com/LizardByte/Sunshine`
- `https://docs.lizardbyte.dev/projects/sunshine/master/md_docs_2configuration.html`
- `https://docs.lizardbyte.dev/projects/sunshine/v0.23.0/about/advanced_usage.html`
- `https://github.com/moonlight-stream/moonlight-docs/wiki/Setup-Guide`
- `https://moonlight-stream.org/`

## Requirements Covered

- `REQ-CTRL-008`: streaming readiness is checked from the effective plan before display topology, app launch, or wrapper start.
- `REQ-CTRL-009`: the client consumes the server plan and stream descriptor; it does not reinterpret wrapper capabilities.
- `REQ-DISP-011`: unsupported stream plans fail explicitly instead of falling back to a physical display or another backend.
- `REQ-SESS-008`: stream startup failures remain explicit and do not leave topology unmanaged.
- `REQ-HDR-004`: HDR prefer can continue SDR when unsupported; this milestone validates only plans that actually request HDR.
- `REQ-HDR-005`: HDR require plans must fail before launch if the wrapper manifest cannot support HDR.
- `REQ-HDR-008`: missing HDR support is reported clearly through preflight errors and diagnostics.
- `REQ-NET-003`: codec, FPS, bitrate, and transport are validated against wrapper capabilities.
- `REQ-REC-008`: diagnostics expose wrapper manifest access and capability failures.
- `REQ-TEST-007`: streaming wrapper readiness is interface-backed and fake-testable.
- `REQ-TEST-010`: real phone testing remains final confirmation.

## Manifest Shape

The external wrapper manifest is JSON:

```json
{
  "name": "Sunshine bridge",
  "protocol": "gamestream",
  "launchUri": "moonlight://beacon/z-fold-7",
  "endpoints": {
    "rtsp": "rtsp://127.0.0.1:48010/beacon",
    "input": "udp://127.0.0.1:48000"
  },
  "codecs": ["av1", "hevc", "h264"],
  "maxFps": 120,
  "maxBitrateMbps": 150,
  "hdr10": false,
  "transports": ["lan-direct", "relay"],
  "encoders": ["nvenc", "amf", "qsv", "software"],
  "capture": ["dxgi", "wgc"],
  "diagnostics": ["Windows.Graphics.Capture unavailable in service mode"]
}
```

Validation rules:

- Missing manifest path means the old external-process behavior remains valid.
- Configured manifest path must exist and parse as JSON.
- If `codecs` is non-empty, `plan.Stream.Codec` must be included.
- If `maxFps` is greater than zero, `plan.Stream.Fps` must not exceed it.
- If `maxBitrateMbps` is greater than zero, `plan.Stream.InitialBitrateMbps` must not exceed it.
- If `transports` is non-empty, `plan.Stream.Transport` must be included.
- If `plan.Display.HdrEnabled` is true, `hdr10` must be true.
- Explicit `ExternalProcessStreamingOptions.Connection*` values override manifest protocol, launch URI, and endpoints.

## File Map

- Modify: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`
  - Add `ManifestPath` to `ExternalProcessStreamingOptions`.
  - Add manifest records and read result records.
  - Inject an `IExternalStreamingManifestReader`.
  - Validate the session plan against the manifest during `CheckReadinessAsync`.
  - Use manifest connection fields when explicit connection config is absent.
  - Pass `BEACON_WRAPPER_MANIFEST_PATH` to the wrapper environment.
- Modify: `src/Beacon.Platform.Windows/Streaming/WindowsExternalStreamingProcessRunner.cs`
  - Keep process-runner behavior unchanged.
- Create: `src/Beacon.Platform.Windows/Streaming/WindowsExternalStreamingManifestReader.cs`
  - Read and parse manifest JSON from disk.
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
  - Bind manifest path from config/env and register the manifest reader.
- Modify: `src/Beacon.Server/appsettings.json`
  - Add empty `ManifestPath` under `Beacon:Streaming:ExternalProcess`.
- Modify: `src/Beacon.Server/appsettings.Development.json`
  - Add empty `ManifestPath` under `Beacon:Streaming:ExternalProcess`.
- Modify: `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`
  - Add manifest reader fake and wrapper readiness tests.
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`
  - Add manifest path binding tests.
- Modify: `README.md`
  - Document manifest configuration and validation.
- Modify: `docs/extraction-map.md`
  - Document that manifest support remains a Beacon-owned wrapper contract.
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-18-wrapper-readiness-manifest.md`
  - Track execution state.

## Task 1: Manifest Reader And External Options

**Files:**
- Modify: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`
- Create: `src/Beacon.Platform.Windows/Streaming/WindowsExternalStreamingManifestReader.cs`
- Test: `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`

- [x] **Step 1: Write failing manifest reader tests**

Add to `ExternalProcessStreamingBackendTests`:

```csharp
[Fact]
public async Task PreflightFailsWhenConfiguredManifestIsMissing()
{
    var reader = new FakeExternalStreamingManifestReader();
    var backend = new ExternalProcessStreamingBackend(
        new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe", ManifestPath: "C:\\Tools\\missing-manifest.json"),
        new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]),
        reader);

    StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

    Assert.False(result.Success);
    Assert.Contains("External streaming manifest 'C:\\Tools\\missing-manifest.json' does not exist", result.Error, StringComparison.Ordinal);
}

[Fact]
public async Task PreflightRejectsPlanUnsupportedByManifest()
{
    var reader = new FakeExternalStreamingManifestReader();
    reader.Manifests["C:\\Tools\\beacon-streaming.json"] = new ExternalStreamingManifest(
        "Sunshine bridge",
        "gamestream",
        null,
        new Dictionary<string, string>(),
        ["h264"],
        60,
        40,
        Hdr10: false,
        ["lan-direct"],
        ["software"],
        ["dxgi"],
        ["AV1 disabled"]);
    var backend = new ExternalProcessStreamingBackend(
        new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe", ManifestPath: "C:\\Tools\\beacon-streaming.json"),
        new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]),
        reader);

    StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

    Assert.False(result.Success);
    Assert.Contains("codec av1 is not supported", result.Error, StringComparison.OrdinalIgnoreCase);
}
```

Extend `FakeExternalStreamingProcessRunner` with a constructor:

```csharp
public FakeExternalStreamingProcessRunner(IEnumerable<string>? existingFiles = null)
{
    if (existingFiles is null)
    {
        return;
    }

    foreach (string path in existingFiles)
    {
        ExistingFiles.Add(path);
    }
}
```

Add this fake reader to the test class:

```csharp
private sealed class FakeExternalStreamingManifestReader : IExternalStreamingManifestReader
{
    public Dictionary<string, ExternalStreamingManifest> Manifests { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => Manifests.ContainsKey(path);

    public ExternalStreamingManifestReadResult Read(string path) =>
        Manifests.TryGetValue(path, out ExternalStreamingManifest? manifest)
            ? ExternalStreamingManifestReadResult.Ok(manifest)
            : ExternalStreamingManifestReadResult.Fail($"External streaming manifest '{path}' does not exist.");
}
```

- [x] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackendTests
```

Expected: compile failures for missing manifest option, manifest records, reader interface, and backend constructor.

- [x] **Step 3: Add manifest contracts**

In `ExternalProcessStreamingBackend.cs`, change options to:

```csharp
public sealed record ExternalProcessStreamingOptions(
    string? ExecutablePath,
    string? ConnectionProtocol = null,
    string? ConnectionLaunchUri = null,
    IReadOnlyDictionary<string, string>? ConnectionEndpoints = null,
    string? ManifestPath = null);
```

Add records/interfaces:

```csharp
public sealed record ExternalStreamingManifest(
    string? Name,
    string? Protocol,
    string? LaunchUri,
    IReadOnlyDictionary<string, string>? Endpoints,
    IReadOnlyList<string>? Codecs,
    int? MaxFps,
    int? MaxBitrateMbps,
    bool Hdr10,
    IReadOnlyList<string>? Transports,
    IReadOnlyList<string>? Encoders,
    IReadOnlyList<string>? Capture,
    IReadOnlyList<string>? Diagnostics);

public sealed record ExternalStreamingManifestReadResult(bool Success, ExternalStreamingManifest? Manifest, string? Error)
{
    public static ExternalStreamingManifestReadResult Ok(ExternalStreamingManifest manifest) => new(true, manifest, null);

    public static ExternalStreamingManifestReadResult Fail(string error) => new(false, null, error);
}

public interface IExternalStreamingManifestReader
{
    bool FileExists(string path);

    ExternalStreamingManifestReadResult Read(string path);
}
```

Change the backend constructor to:

```csharp
public sealed class ExternalProcessStreamingBackend(
    ExternalProcessStreamingOptions options,
    IExternalStreamingProcessRunner runner,
    IExternalStreamingManifestReader manifestReader) : IStreamingBackend
```

- [x] **Step 4: Implement Windows manifest reader**

Create `src/Beacon.Platform.Windows/Streaming/WindowsExternalStreamingManifestReader.cs`:

```csharp
using System.Text.Json;

namespace Beacon.Platform.Windows.Streaming;

public sealed class WindowsExternalStreamingManifestReader : IExternalStreamingManifestReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool FileExists(string path) => File.Exists(path);

    public ExternalStreamingManifestReadResult Read(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            ExternalStreamingManifest? manifest = JsonSerializer.Deserialize<ExternalStreamingManifest>(json, JsonOptions);
            return manifest is null
                ? ExternalStreamingManifestReadResult.Fail($"External streaming manifest '{path}' is empty or invalid.")
                : ExternalStreamingManifestReadResult.Ok(manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ExternalStreamingManifestReadResult.Fail($"External streaming manifest '{path}' could not be read: {ex.Message}");
        }
    }
}
```

- [x] **Step 5: Validate manifest during preflight**

In `CheckReadinessAsync`, after executable checks:

```csharp
ExternalStreamingManifestReadResult manifest = ReadManifestIfConfigured();
if (!manifest.Success)
{
    return Task.FromResult(StreamingPreflightResult.Fail(manifest.Error ?? "External streaming manifest is invalid."));
}

if (manifest.Manifest is not null)
{
    string? compatibilityError = ValidateManifest(plan, manifest.Manifest);
    if (!string.IsNullOrWhiteSpace(compatibilityError))
    {
        return Task.FromResult(StreamingPreflightResult.Fail(compatibilityError));
    }
}
```

Add helpers:

```csharp
private ExternalStreamingManifestReadResult ReadManifestIfConfigured()
{
    if (string.IsNullOrWhiteSpace(options.ManifestPath))
    {
        return ExternalStreamingManifestReadResult.Ok(EmptyManifest);
    }

    string manifestPath = options.ManifestPath.Trim();
    if (!manifestReader.FileExists(manifestPath))
    {
        return ExternalStreamingManifestReadResult.Fail($"External streaming manifest '{manifestPath}' does not exist.");
    }

    return manifestReader.Read(manifestPath);
}

private static readonly ExternalStreamingManifest EmptyManifest = new(
    null,
    null,
    null,
    new Dictionary<string, string>(),
    [],
    null,
    null,
    Hdr10: false,
    [],
    [],
    [],
    []);

private static string? ValidateManifest(SessionPlan plan, ExternalStreamingManifest manifest)
{
    if (ContainsValues(manifest.Codecs) && !Contains(manifest.Codecs, plan.Stream.Codec))
    {
        return $"External streaming manifest does not support codec {plan.Stream.Codec}.";
    }

    if (manifest.MaxFps is > 0 && plan.Stream.Fps > manifest.MaxFps.Value)
    {
        return $"External streaming manifest supports up to {manifest.MaxFps.Value} FPS, but the plan requires {plan.Stream.Fps} FPS.";
    }

    if (manifest.MaxBitrateMbps is > 0 && plan.Stream.InitialBitrateMbps > manifest.MaxBitrateMbps.Value)
    {
        return $"External streaming manifest supports up to {manifest.MaxBitrateMbps.Value} Mbps, but the plan requires {plan.Stream.InitialBitrateMbps} Mbps.";
    }

    if (ContainsValues(manifest.Transports) && !Contains(manifest.Transports, plan.Stream.Transport))
    {
        return $"External streaming manifest does not support transport {plan.Stream.Transport}.";
    }

    if (plan.Display.HdrEnabled && !manifest.Hdr10)
    {
        return "External streaming manifest does not support HDR10 required by the plan.";
    }

    return null;
}

private static bool ContainsValues(IReadOnlyList<string>? values) =>
    values is { Count: > 0 } && values.Any(value => !string.IsNullOrWhiteSpace(value));

private static bool Contains(IReadOnlyList<string>? values, string expected) =>
    values?.Any(value => value.Equals(expected, StringComparison.OrdinalIgnoreCase)) == true;
```

- [x] **Step 6: Verify green for new preflight tests**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackendTests
```

Expected: platform streaming tests pass.

## Task 2: Manifest-Derived Connection Descriptor

**Files:**
- Modify: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`
- Test: `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`

- [ ] **Step 1: Write failing descriptor test**

Add:

```csharp
[Fact]
public async Task StartUsesManifestConnectionWhenExplicitConnectionIsAbsent()
{
    var reader = new FakeExternalStreamingManifestReader();
    reader.Manifests["C:\\Tools\\beacon-streaming.json"] = new ExternalStreamingManifest(
        "Sunshine bridge",
        "gamestream",
        "moonlight://beacon/z-fold-7-steam-shortcut:3767414131",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rtsp"] = "rtsp://127.0.0.1:48010/beacon"
        },
        ["av1", "hevc", "h264"],
        120,
        150,
        Hdr10: false,
        ["lan-direct"],
        ["nvenc"],
        ["dxgi"],
        ["ready"]);
    var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
    var backend = new ExternalProcessStreamingBackend(
        new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe", ManifestPath: "C:\\Tools\\beacon-streaming.json"),
        runner,
        reader);

    StreamingStartResult result = await backend.StartAsync(CreatePlan(), CancellationToken.None);

    StreamingSessionState session = Assert.IsType<StreamingSessionState>(result.Session);
    Assert.NotNull(session.Connection);
    Assert.Equal("gamestream", session.Connection.Protocol);
    Assert.Equal("moonlight://beacon/z-fold-7-steam-shortcut:3767414131", session.Connection.LaunchUri);
    Assert.Contains(session.Connection.Endpoints, endpoint => endpoint.Role == "rtsp" && endpoint.Uri == "rtsp://127.0.0.1:48010/beacon");
    Assert.Equal("C:\\Tools\\beacon-streaming.json", session.Connection.Metadata["manifestPath"]);
    ExternalStreamingCommand command = Assert.Single(runner.StartedCommands);
    Assert.Equal("C:\\Tools\\beacon-streaming.json", command.Environment["BEACON_WRAPPER_MANIFEST_PATH"]);
}
```

- [ ] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter StartUsesManifestConnectionWhenExplicitConnectionIsAbsent
```

Expected: fail because manifest connection fields are not used yet.

- [ ] **Step 3: Use manifest descriptor fields**

Change `CreateConnectionDescriptor` to accept `ExternalStreamingManifest? manifest`:

```csharp
private static StreamingConnectionDescriptor? CreateConnectionDescriptor(
    ExternalProcessStreamingOptions options,
    ExternalStreamingManifest? manifest)
```

Resolve fields:

```csharp
string? protocolSource = string.IsNullOrWhiteSpace(options.ConnectionProtocol)
    ? manifest?.Protocol
    : options.ConnectionProtocol;
string? launchUriSource = string.IsNullOrWhiteSpace(options.ConnectionLaunchUri)
    ? manifest?.LaunchUri
    : options.ConnectionLaunchUri;
IReadOnlyDictionary<string, string>? endpointSource = options.ConnectionEndpoints is { Count: > 0 }
    ? options.ConnectionEndpoints
    : manifest?.Endpoints;
```

Use these sources when building the descriptor. Add metadata:

```csharp
var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
if (!string.IsNullOrWhiteSpace(options.ManifestPath))
{
    metadata["manifestPath"] = options.ManifestPath.Trim();
}

if (!string.IsNullOrWhiteSpace(manifest?.Name))
{
    metadata["manifestName"] = manifest.Name.Trim();
}
```

Pass metadata to `StreamingConnectionDescriptor`.

- [ ] **Step 4: Read manifest once in start**

In `StartAsync`, after preflight success, read the manifest:

```csharp
ExternalStreamingManifest? manifest = ReadManifestIfConfigured().Manifest;
ExternalStreamingCommand command = CreateStartCommand(options.ExecutablePath!, plan, options);
...
CreateConnectionDescriptor(options, manifest)
```

In `CreateStartCommand`, add:

```csharp
if (!string.IsNullOrWhiteSpace(options?.ManifestPath))
{
    environment["BEACON_WRAPPER_MANIFEST_PATH"] = options.ManifestPath.Trim();
}
```

- [ ] **Step 5: Keep explicit connection overrides**

Extend `StartIncludesConfiguredConnectionDescriptor` to set both explicit connection values and a manifest with different values, then assert explicit values win:

```csharp
reader.Manifests["C:\\Tools\\beacon-streaming.json"] = new ExternalStreamingManifest(
    "ignored",
    "manifest-protocol",
    "manifest://ignored",
    new Dictionary<string, string> { ["ignored"] = "manifest://endpoint" },
    ["av1"],
    120,
    150,
    Hdr10: false,
    ["lan-direct"],
    [],
    [],
    []);
```

Create options with `ManifestPath: "C:\\Tools\\beacon-streaming.json"` plus the existing explicit protocol/launch/endpoints. Assert the session still uses `gamestream`, `moonlight://...`, and explicit endpoints.

- [ ] **Step 6: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackendTests
```

Expected: all external process streaming tests pass.

## Task 3: Service Registration And Configuration

**Files:**
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `src/Beacon.Server/appsettings.json`
- Modify: `src/Beacon.Server/appsettings.Development.json`
- Test: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`

- [ ] **Step 1: Write failing registration test**

Add:

```csharp
[Fact]
public void ExternalProcessManifestPathUsesConfiguration()
{
    using ServiceProvider provider = BuildProvider(
        new KeyValuePair<string, string?>(BeaconServiceRegistration.StreamingBackendConfigurationKey, "external-process"),
        new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingExecutableConfigurationKey, "C:\\Tools\\sunshine-wrapper.exe"),
        new KeyValuePair<string, string?>(BeaconServiceRegistration.ExternalStreamingManifestConfigurationKey, "C:\\Tools\\beacon-streaming.json"));

    ExternalProcessStreamingOptions options = provider.GetRequiredService<ExternalProcessStreamingOptions>();

    Assert.Equal("C:\\Tools\\beacon-streaming.json", options.ManifestPath);
    Assert.IsType<WindowsExternalStreamingManifestReader>(provider.GetRequiredService<IExternalStreamingManifestReader>());
}
```

- [ ] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ExternalProcessManifestPathUsesConfiguration
```

Expected: compile failure for missing configuration constant or manifest reader registration.

- [ ] **Step 3: Add config/env constants**

In `BeaconServiceRegistration`, add:

```csharp
public const string ExternalStreamingManifestConfigurationKey = "Beacon:Streaming:ExternalProcess:ManifestPath";
public const string ExternalStreamingManifestEnvironmentVariable = "BEACON_EXTERNAL_STREAMING_MANIFEST";
```

Pass `Environment.GetEnvironmentVariable(ExternalStreamingManifestEnvironmentVariable)` from the public `AddBeaconServices(configuration)` overload into the internal overload.

Add `string? environmentExternalStreamingManifest = null` to the internal overload and `AddStreamingBackend`.

- [ ] **Step 4: Bind manifest path into options**

Update `CreateExternalProcessStreamingOptions` signature to include `environmentExternalStreamingManifest` and resolve:

```csharp
string? manifestPath = string.IsNullOrWhiteSpace(environmentExternalStreamingManifest)
    ? configuration[ExternalStreamingManifestConfigurationKey]
    : environmentExternalStreamingManifest;
```

Return:

```csharp
return new ExternalProcessStreamingOptions(
    ResolveExternalStreamingExecutable(configuration, environmentExternalStreamingExecutable),
    protocol,
    launchUri,
    endpoints,
    manifestPath);
```

Register:

```csharp
services.AddSingleton<IExternalStreamingManifestReader, WindowsExternalStreamingManifestReader>();
```

- [ ] **Step 5: Add appsettings keys**

In both `src/Beacon.Server/appsettings.json` and `src/Beacon.Server/appsettings.Development.json`, add:

```json
"ManifestPath": ""
```

under `Beacon.Streaming.ExternalProcess`.

- [ ] **Step 6: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter BeaconServiceRegistrationTests
```

Expected: all service registration tests pass.

## Task 4: Server Preflight Behavior And Diagnostics

**Files:**
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Modify: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`

- [ ] **Step 1: Write failing launch preflight test**

Add to `ClientApiTests`:

```csharp
[Fact]
public async Task LaunchStopsBeforeDisplayLeaseWhenExternalManifestRejectsPlan()
{
    var display = new FakeDisplayBackend();
    var launcher = new FakeGameLauncher();
    var runner = new FakeExternalStreamingProcessRunner(["C:\\Tools\\sunshine-wrapper.exe"]);
    var reader = new FakeExternalStreamingManifestReader();
    reader.Manifests["C:\\Tools\\beacon-streaming.json"] = new ExternalStreamingManifest(
        "Sunshine bridge",
        "gamestream",
        null,
        new Dictionary<string, string>(),
        ["h264"],
        60,
        40,
        Hdr10: false,
        ["lan-direct"],
        ["software"],
        ["dxgi"],
        ["AV1 disabled"]);
    var backend = new ExternalProcessStreamingBackend(
        new ExternalProcessStreamingOptions("C:\\Tools\\sunshine-wrapper.exe", ManifestPath: "C:\\Tools\\beacon-streaming.json"),
        runner,
        reader);
    WebApplicationFactory<Program> failingFactory = factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDisplayBackend>();
            services.RemoveAll<IGameLauncher>();
            services.RemoveAll<IStreamingBackend>();
            services.AddSingleton<IDisplayBackend>(display);
            services.AddSingleton<IGameLauncher>(launcher);
            services.AddSingleton<IStreamingBackend>(backend);
        }));
    HttpClient client = failingFactory.CreateClient();

    HttpResponseMessage response = await client.PostAsJsonAsync("/clients/z-fold-7/launch", new
    {
        gameId = "steam-shortcut:3767414131"
    });

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    string body = await response.Content.ReadAsStringAsync();
    Assert.Contains("codec av1 is not supported", body, StringComparison.OrdinalIgnoreCase);
    Assert.Empty(display.EnsureCalls);
    Assert.Empty(launcher.Requests);
    Assert.Empty(runner.StartedCommands);
}
```

Add local fake reader/runner helpers or move existing test fakes into shared test files only if duplication becomes noisy.

- [ ] **Step 2: Verify red or green**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter LaunchStopsBeforeDisplayLeaseWhenExternalManifestRejectsPlan
```

Expected: pass if Task 1 already made `CheckReadinessAsync` authoritative, otherwise fail because launch reaches display/launcher.

- [ ] **Step 3: Fix launch preflight if needed**

If the test fails, ensure `ClientEndpoints` still calls `streaming.CheckReadinessAsync(planResult.Plan, cancellationToken)` before `leases.EnsureLeaseAsync(...)` and returns a `503` problem on failure.

- [ ] **Step 4: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
```

Expected: all client API tests pass.

## Task 5: Docs, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-18-wrapper-readiness-manifest.md`

- [ ] **Step 1: Update docs**

Add to `README.md` under streaming backend notes:

```markdown
External-process mode may also read a wrapper manifest from `Beacon:Streaming:ExternalProcess:ManifestPath` or `BEACON_EXTERNAL_STREAMING_MANIFEST`. When present, Beacon validates codec, FPS, bitrate, transport, and HDR support against the manifest during streaming preflight, before display or launch side effects. Manifest connection fields can provide the stream descriptor unless explicit connection settings override them.
```

Update the Sunshine row in `docs/extraction-map.md` to mention that Milestone 18 adds a Beacon-owned wrapper manifest contract and still copies no upstream streaming source.

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

Expected: all commands pass; the Gradle 9 deprecation warning remains acceptable if the command exits successfully.

- [ ] **Step 4: Boundary audit**

Run:

```powershell
rg "LizardByte|Sunshine/src|nvhttp|moonlight-stream|moonlight" src tests -n
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
```

Expected: upstream terms only appear in docs/test URI literals; no copied upstream source appears under `src`; no timeout/cancellation helper is introduced.

- [ ] **Step 5: Commit and sync**

Run:

```powershell
git add README.md docs/extraction-map.md docs/superpowers/plans/2026-07-08-beacon-stream-milestone-18-wrapper-readiness-manifest.md src tests
git commit -m "Add external wrapper readiness manifests"
git push -u origin codex/milestone-18-wrapper-readiness-manifest
gh pr create --draft --base main --head codex/milestone-18-wrapper-readiness-manifest --title "Add external wrapper readiness manifests" --body "Milestone 18 wrapper readiness manifest implementation."
$pr = gh pr view --json number --jq .number
gh pr checks $pr --watch
gh pr ready $pr
gh pr merge $pr --merge --delete-branch
```

Expected: PR checks pass, PR merges, and local `main` is clean.

## Non-Goals

- No native video decoder.
- No Android input forwarding.
- No copied Sunshine, Moonlight, Apollo, Vibeshine, or Vibepollo source.
- No executing a wrapper solely to query readiness.
- No timeout-based manifest or process cancellation.
- No real Sunshine pairing implementation.
