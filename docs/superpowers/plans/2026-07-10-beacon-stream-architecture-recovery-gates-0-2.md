# Beacon Stream Architecture Recovery Gates 0–2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove compatibility-first streaming architecture from Beacon Core, Server, Cockpit, solution structure, and Android while preserving the proven control plane, display lifecycle, application catalog, ownership, recovery, local settings, benchmark inputs, and generic decoder primitives.

**Architecture:** Recovery is deletion-first. Core is reduced to a protocol-neutral `IStreamingBackend` boundary, fake-host tests keep the orchestrator executable, Windows host mode fails closed until the Beacon-owned StreamWorker exists, and Android is reduced to control-plane/catalog/local-settings functionality with no media route. Gate 3 source selection and StreamWorker implementation are deliberately planned only after this repository state is clean.

**Tech Stack:** .NET 10, ASP.NET Core, WPF, xUnit, Java 17, Android SDK 35, Gradle 8.14.1, JUnit 4, React/TypeScript Client Lab, Playwright, GitHub Actions.

---

## Scope And Sync Sequence

This specification spans independent subsystems, so implementation is divided into four coherent checkpoints:

1. **Checkpoint A:** machine-checked debt inventory and protected behavior baseline.
2. **Checkpoint B:** protocol-neutral Core plus removal of external/server/cockpit compatibility.
3. **Checkpoint C:** Android compatibility deletion and submodule removal.
4. **Checkpoint D:** documentation cleanup, negative boundary checks, full validation, refactor, and second validation.

Each checkpoint is committed and pushed. Checkpoints B and C receive a focused post-sync refactor only when review identifies a concrete defect; no abstraction is added merely to preserve deleted behavior.

## Specification Coverage

This plan fully targets Recovery Gates 0–2 and the deletion requirements in `REQ-BOUND-002` through `REQ-BOUND-006`, `REQ-STREAM-010`, `REQ-STREAM-013`, and the Architecture Recovery acceptance gate. It preserves the existing implementation evidence for `REQ-DISP-*`, `REQ-SESS-*`, `REQ-GAME-*`, `REQ-REC-*`, and local-only APK settings.

This plan does **not** claim completion of the full objective. `REQ-BOUND-001`, client security/tickets, `REQ-BENCH-*`, `REQ-NET-*`, `REQ-HW-*`, the production portions of `REQ-STREAM-*`, real StreamWorker/StreamCore, and the minimal/physical streaming acceptance gates remain mandatory. Task 9 explicitly hands those requirements to source-audit-backed Gate 3–5 plans after the compatibility code is gone.

## Task 1: Record And Guard The Existing Debt Boundary

**Files:**

- Create: `tests/Beacon.Core.Tests/Architecture/ArchitectureRecoveryBoundaryTests.cs`
- Create: `tests/Beacon.Core.Tests/Architecture/architecture-recovery-debt.txt`
- Read: `docs/source-audits/2026-07-10-beacon-architecture-recovery-inventory.md`
- Test: `tests/Beacon.Core.Tests/Architecture/ArchitectureRecoveryBoundaryTests.cs`

- [ ] **Step 1: Add repository-root discovery and protected behavior assertions**

Create a test fixture using the same root-discovery approach as `ReadmeLinkTests`. The first test must assert that the recovery inventory and authoritative specification exist. The second test must assert the protected directories still exist:

```csharp
private static readonly string[] ProtectedDirectories =
[
    "src/Beacon.Core/Clients",
    "src/Beacon.Core/Displays",
    "src/Beacon.Core/Games",
    "src/Beacon.Core/Recovery",
    "src/Beacon.Core/Sessions",
    "src/Beacon.Platform.Windows/Displays",
    "src/Beacon.Platform.Windows/Games",
    "src/Beacon.Platform.Windows/Recovery",
    "src/Beacon.Android/app/src/main/java/dev/beacon/android"
];

[Fact]
public void RecoveryAuthorityAndProtectedBoundariesExist()
{
    string root = FindRepositoryRoot();
    Assert.True(File.Exists(Path.Combine(root,
        "docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md")));
    Assert.True(File.Exists(Path.Combine(root,
        "docs/source-audits/2026-07-10-beacon-architecture-recovery-inventory.md")));

    foreach (string relative in ProtectedDirectories)
    {
        Assert.True(Directory.Exists(Path.Combine(root, relative)), relative);
    }
}
```

- [ ] **Step 2: Add an exact current-debt snapshot**

Generate the candidate list with this read-only command, inspect it, and add the sorted repository-relative results to `architecture-recovery-debt.txt` using `apply_patch`:

```powershell
$paths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($root in @(
    'src/Beacon.Platform.Windows/Streaming',
    'src/Beacon.StreamingProbe',
    'tests/Beacon.StreamingProbe.Tests',
    'src/Beacon.Android/streaming-moonlight')) {
    git ls-files "$root/**" | ForEach-Object { [void]$paths.Add(($_ -replace '\\', '/')) }
}

rg -l -i 'Moonlight|GameStream|Rtsp|Rtp|ExternalProcessStreaming|StreamingWrapper|WrapperChild|RuntimeDescriptor|LaunchUri|nativeSession|beacon-test' `
  src/Beacon.Core src/Beacon.Server src/Beacon.Cockpit src/Beacon.Android/app/src `
  tests/Beacon.Core.Tests tests/Beacon.Server.Tests tests/Beacon.Cockpit.Tests tests/Beacon.Platform.Windows.Tests `
  --glob '*.{cs,java,xml,gradle,json}' |
    ForEach-Object { [void]$paths.Add(($_ -replace '\\', '/')) }

$paths | Sort-Object
```

Do not include generic `Manifest` or `EncodedVideo` matches: Android manifests, Steam manifests, and generic decoder primitives are retained.

- [ ] **Step 3: Add a current-debt test that fails on additions or stale entries**

Scan these protected runtime files for `Moonlight`, `GameStream`, `Rtsp`, `Rtp`, `ExternalProcessStreaming`, `StreamingWrapper`, `WrapperChild`, `RuntimeDescriptor`, `LaunchUri`, `nativeSession`, and `beacon-test`:

```text
src/Beacon.Core/**/*.cs
src/Beacon.Server/**/*.cs
src/Beacon.Cockpit/**/*.cs
src/Beacon.Android/app/src/main/java/**/*.java
```

Also add every file under the four exact compatibility roots used by Step 2. Load the snapshot into a `HashSet<string>` and assert set equality:

```csharp
Assert.True(
    actual.SetEquals(expected),
    $"Architecture debt changed. Added: {string.Join(", ", actual.Except(expected))}; " +
    $"removed but not acknowledged: {string.Join(", ", expected.Except(actual))}");
```

Every deletion task must remove deleted paths from the snapshot in the same commit. This prevents new debt and prevents the ledger from becoming stale.

- [ ] **Step 4: Run the focused guard test**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter ArchitectureRecoveryBoundaryTests
```

Expected: PASS against the current debt inventory.

- [ ] **Step 5: Capture the protected baseline**

Run:

```powershell
dotnet test Beacon.slnx --no-restore
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src/Beacon.Android test assembleDebug
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Expected baseline: all existing tests and builds pass before deletion.

- [ ] **Step 6: Commit and sync Checkpoint A**

```powershell
git add docs/source-audits/2026-07-10-beacon-architecture-recovery-inventory.md tests/Beacon.Core.Tests/Architecture
git commit -m "test: guard Beacon architecture recovery boundaries"
git push
```

## Task 2: Make Core Streaming Contracts Protocol-Neutral

**Files:**

- Modify: `src/Beacon.Core/Streaming/IStreamingBackend.cs`
- Modify: `src/Beacon.Core/Streaming/StreamingSessionState.cs`
- Modify: `src/Beacon.Core/Streaming/FakeStreamingBackend.cs`
- Create: `src/Beacon.Core/Streaming/UnavailableStreamingBackend.cs`
- Delete: `src/Beacon.Core/Streaming/MoonlightNativeSessionDescriptor.cs`
- Delete: `src/Beacon.Core/Streaming/BeaconTestStreamingBackend.cs`
- Modify: `tests/Beacon.Core.Tests/Streaming/FakeStreamingBackendTests.cs`
- Create: `tests/Beacon.Core.Tests/Streaming/UnavailableStreamingBackendTests.cs`
- Delete: `tests/Beacon.Core.Tests/Streaming/MoonlightNativeSessionDescriptorTests.cs`
- Delete: `tests/Beacon.Core.Tests/Streaming/BeaconTestStreamingBackendTests.cs`

- [ ] **Step 1: Rewrite focused tests for the target health and session shape**

`FakeStreamingBackendTests` must assert that health and session state contain only Beacon facts:

```csharp
StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);
Assert.True(health.Ready);
Assert.Equal("ready", health.State);
Assert.Equal(["h264", "hevc", "av1"], health.Capabilities.Codecs);
Assert.Equal(["fake"], health.Capabilities.Encoders);
Assert.Equal(["fake"], health.Capabilities.CaptureMethods);
Assert.Equal(120, health.Capabilities.MaxFps);

StreamingStartResult start = await backend.StartAsync(plan, CancellationToken.None);
StreamingSessionState session = Assert.IsType<StreamingSessionState>(start.Session);
Assert.Equal(plan.SessionId, session.SessionId);
Assert.Equal(plan.Display.DisplayId, session.DisplayId);
Assert.Equal("running", session.State);
```

The test must not mention connection descriptors, endpoints, launch URIs, protocols, wrappers, manifests, executables, or native sessions.

- [ ] **Step 2: Add failing tests for the fail-closed Windows placeholder**

```csharp
[Fact]
public async Task FailsPreflightUntilBeaconStreamWorkerExists()
{
    var backend = new UnavailableStreamingBackend();
    StreamingBackendHealth health = await backend.GetHealthAsync(CancellationToken.None);
    StreamingPreflightResult preflight = await backend.CheckReadinessAsync(
        CreatePlan(), CancellationToken.None);

    Assert.False(health.Ready);
    Assert.Equal("unavailable", health.State);
    Assert.False(preflight.Success);
    Assert.Contains("StreamWorker", preflight.Error, StringComparison.Ordinal);
    Assert.Empty(backend.GetSessions());
}
```

Add the same `CreatePlan()` helper used by `FakeStreamingBackendTests`:

```csharp
private static SessionPlan CreatePlan() => new(
    SessionId: "z-fold-7-steam-shortcut:3767414131",
    ClientId: new ClientId("z-fold-7"),
    AppId: "steam-shortcut:3767414131",
    Display: new PlannedDisplay(
        "client-z-fold-7", 2560, 1600, 120, "virtual-primary",
        HdrPreference.Prefer, HdrEnabled: false, "sdr",
        "HDR unavailable during the recovery fixture."),
    Stream: new PlannedStream("h264", 120, 65, "beacon", "measured"));
```

Run the focused tests. Expected: FAIL because the target records and `UnavailableStreamingBackend` do not exist.

- [ ] **Step 3: Replace the health contract**

Use this target shape in `IStreamingBackend.cs`:

```csharp
public sealed record StreamingCapabilities(
    IReadOnlyList<string> Codecs,
    IReadOnlyList<string> Encoders,
    IReadOnlyList<string> CaptureMethods,
    int? MaxFps,
    int? MaxBitrateMbps,
    bool Hdr10);

public sealed record StreamingBackendHealth(
    bool Ready,
    string State,
    string Diagnostic,
    StreamingCapabilities Capabilities,
    int ActiveSessions,
    IReadOnlyList<string> Diagnostics)
{
    public static StreamingBackendHealth Unknown(string diagnostic) => new(
        Ready: false,
        State: "unknown",
        Diagnostic: diagnostic,
        Capabilities: new([], [], [], null, null, false),
        ActiveSessions: 0,
        Diagnostics: []);
}
```

Remove `GetNativeSessionAsync`, `GetClientSessionAsync`, and `StreamingClientSessionSnapshot` from `IStreamingBackend`.

- [ ] **Step 4: Replace the session state contract**

Use this target shape in `StreamingSessionState.cs`:

```csharp
public sealed record StreamingSessionState(
    string SessionId,
    string ClientId,
    string AppId,
    string DisplayId,
    string Codec,
    int Fps,
    int InitialBitrateMbps,
    string State,
    string? Error);
```

Delete `StreamingConnectionDescriptor` and `StreamingEndpointDescriptor`. Transport remains in the immutable `SessionPlan`; it is not a user-selectable backend handoff.

- [ ] **Step 5: Simplify the fake backend**

Return `StreamingBackendHealth` with state `ready`, generic fake capabilities, and no handoff metadata. Build `StreamingSessionState` directly from the plan and retain existing deterministic start/stop failure controls.

- [ ] **Step 6: Add the fail-closed backend**

`UnavailableStreamingBackend` implements `IStreamingBackend`. It returns:

- health `Ready=false`, `State="unavailable"`;
- diagnostic `Beacon StreamWorker is not implemented during architecture recovery.`;
- failed preflight and start with that exact diagnostic;
- failed stop unless a future worker session exists;
- no sessions.

It must never fabricate a running stream.

- [ ] **Step 7: Delete protocol and diagnostic backend files**

Delete the four production/test files listed above. Update the architecture debt allowlist so removed files are no longer accepted.

- [ ] **Step 8: Run Core tests and format**

```powershell
dotnet format Beacon.slnx --verify-no-changes --no-restore
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --no-restore
```

Expected: PASS with no upstream-protocol type in `src/Beacon.Core`.

- [ ] **Step 9: Commit the Core boundary**

```powershell
git add src/Beacon.Core tests/Beacon.Core.Tests
git commit -m "refactor: restore protocol-neutral streaming core"
git push
```

## Task 3: Remove External Streaming And Backend Selection

**Files:**

- Delete: `src/Beacon.Platform.Windows/Streaming/*`
- Delete: `tests/Beacon.Platform.Windows.Tests/Streaming/*`
- Delete: `src/Beacon.StreamingProbe/*`
- Delete: `tests/Beacon.StreamingProbe.Tests/*`
- Modify: `Beacon.slnx`
- Modify: `tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj`
- Delete: `src/Beacon.Server/Hosting/BeaconStreamingBackendMode.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconHostOptions.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `src/Beacon.Server/appsettings.json`
- Modify: `src/Beacon.Server/appsettings.Development.json`
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`

- [ ] **Step 1: Replace registration tests before implementation**

Delete tests for mode parsing, executable paths, wrapper children, manifests, argument templates, Sunshine profiles, launch URIs, endpoint maps, and `beacon-test` mode.

Add these assertions:

```csharp
[Theory]
[InlineData(BeaconHostMode.Fake, typeof(FakeStreamingBackend))]
[InlineData(BeaconHostMode.Windows, typeof(UnavailableStreamingBackend))]
public void RegistersExactlyOneStreamingBoundaryForHostMode(
    BeaconHostMode mode,
    Type expectedType)
{
    string configuredMode = mode == BeaconHostMode.Windows ? "windows" : "fake";
    using ServiceProvider provider = BuildProvider(new KeyValuePair<string, string?>(
        BeaconServiceRegistration.HostModeConfigurationKey,
        configuredMode));
    IStreamingBackend backend = provider.GetRequiredService<IStreamingBackend>();
    Assert.IsType(expectedType, backend);
    Assert.Single(provider.GetServices<IStreamingBackend>());
}
```

Add a source-level test asserting no configuration key starts with `Beacon:Streaming:ExternalProcess` and no environment variable starts with `BEACON_EXTERNAL_STREAMING_`.

Run `BeaconServiceRegistrationTests`. Expected: FAIL against current registration.

- [ ] **Step 2: Remove backend selection from host options**

Change `BeaconHostOptions` to accept only `BeaconHostMode`. Preserve display, launcher, and activity inspector names. Set `StreamingBackendName` from host mode:

```csharp
string streamingBackendName = mode == BeaconHostMode.Windows
    ? nameof(UnavailableStreamingBackend)
    : nameof(FakeStreamingBackend);
```

Delete `StreamingBackendMode` and `StreamingBackendModeName`.

- [ ] **Step 3: Collapse service registration**

Remove all streaming backend configuration/environment parameters and helper methods. Register exactly one implementation after host boundaries:

```csharp
if (mode == BeaconHostMode.Windows)
{
    services.AddSingleton<IStreamingBackend, UnavailableStreamingBackend>();
}
else
{
    services.AddSingleton<IStreamingBackend, FakeStreamingBackend>();
}
```

No executable, manifest, endpoint, launch URI, child process, argument template, test-stream kind, or Sunshine configuration remains.

- [ ] **Step 4: Remove projects and external platform code**

Delete the listed platform streaming and probe files. Remove both probe projects from `Beacon.slnx` and remove the server-test project reference.

Do not replace them with another wrapper or process runner.

- [ ] **Step 5: Remove checked configuration**

Delete every `Beacon:Streaming:Backend`, `ExternalProcess`, and `BeaconTest` setting from both appsettings files. Keep host-mode, display, game, persistence, and artwork configuration.

- [ ] **Step 6: Shrink the architecture debt allowlist**

Remove `src/Beacon.Platform.Windows/Streaming`, `src/Beacon.StreamingProbe`, and `tests/Beacon.StreamingProbe.Tests` from the temporary allowed roots.

- [ ] **Step 7: Validate solution structure and registration**

```powershell
dotnet restore Beacon.slnx
dotnet format Beacon.slnx --verify-no-changes --no-restore
dotnet build Beacon.slnx -warnaserror --no-restore
dotnet test Beacon.slnx --no-build
```

Expected: all .NET projects pass and no StreamingProbe project remains.

- [ ] **Step 8: Commit and sync external-path deletion**

```powershell
git add Beacon.slnx src/Beacon.Platform.Windows src/Beacon.Server tests
git commit -m "refactor: remove external streaming compatibility"
git push
```

## Task 4: Remove Compatibility From Server APIs And Cockpit

**Files:**

- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`
- Delete: `src/Beacon.Server/Api/StreamAssetEndpoints.cs`
- Modify: `src/Beacon.Server/Program.cs`
- Modify: `src/Beacon.Server/Beacon.Server.csproj`
- Delete: `src/Beacon.Server/Assets/beacon-test-color-bars.h264`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitModels.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs`
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs`
- Modify: `tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs`
- Modify: `tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs`

- [ ] **Step 1: Rewrite API tests for protocol-neutral responses**

Launch and stream-status assertions must require only the stream session:

```csharp
JsonElement root = await response.Content.ReadFromJsonAsync<JsonElement>();
JsonElement stream = root.GetProperty("stream");
Assert.Equal("running", stream.GetProperty("state").GetString());
Assert.False(root.TryGetProperty("nativeSession", out _));
Assert.False(stream.TryGetProperty("connection", out _));
```

Admin health tests must assert `ready`, `state`, `diagnostic`, `capabilities`, `activeSessions`, and `diagnostics`, and must reject wrapper/executable/manifest/protocol/launch-URI/endpoint properties.

Run focused tests. Expected: FAIL against the current API models.

- [ ] **Step 2: Simplify client endpoints**

After `streaming.StartAsync`, return `streamResult.Session` directly. Remove calls to `GetClientSessionAsync` and remove `nativeSession` from launch and GET stream responses.

Do not change launch ordering, ownership recording, stream-stop failure handling, disconnect semantics, quit semantics, or display cleanup.

- [ ] **Step 3: Remove diagnostic stream assets**

Delete `StreamAssetEndpoints`, its `Program.cs` mapping, H.264 content inclusion, the asset, and asset endpoint tests. Diagnostic media will return through the one StreamCore path in Gate 4, not an HTTP alternate route.

- [ ] **Step 4: Simplify cockpit models and summaries**

Remove connection models and all wrapper/executable/manifest/protocol/launch-URI/endpoint fields. Format streaming health as:

```text
{state}: {diagnostic}; {activeSessions} active; codecs {codecs}; encoders {encoders}; capture {captureMethods}
```

The session list shows session id, app id, display id, codec, FPS, bitrate, and state. It does not show a connection URI.

- [ ] **Step 5: Shrink the temporary allowlist**

Remove the server API, hosting, and cockpit compatibility files from the allowlist. The protected boundary scan must now find no forbidden term in Core, Server, or Cockpit runtime files.

- [ ] **Step 6: Run server and cockpit tests**

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --no-restore
dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj --no-restore
```

Expected: PASS while launch ownership, stop, disconnect, quit, recovery, and generic diagnostics remain covered.

- [ ] **Step 7: Commit and sync Checkpoint B**

```powershell
git add src/Beacon.Server src/Beacon.Cockpit tests/Beacon.Server.Tests tests/Beacon.Cockpit.Tests tests/Beacon.Core.Tests/Architecture
git commit -m "refactor: remove compatibility from server surfaces"
git push
```

## Task 5: Collapse Android To Control Plane And Generic Decoder Primitives

**Files:**

- Modify: `src/Beacon.Android/settings.gradle`
- Modify: `src/Beacon.Android/app/build.gradle`
- Modify: `.gitmodules`
- Modify: `.github/workflows/ci.yml`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModelSession.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
- Modify: corresponding ViewModel, session, and Activity tests
- Delete: compatibility families listed in the recovery inventory
- Delete: `src/Beacon.Android/streaming-moonlight`
- Delete: `src/Beacon.Android/app/src/androidTest/java/dev/beacon/android/MoonlightNativeCoreInstrumentedTest.java`

- [ ] **Step 1: Rewrite ViewModel tests for the thin-client recovery state**

Tests must prove:

- game catalog loading and selection remain;
- preflight and launch requests remain;
- launch records the server response without parsing a connection;
- stop, disconnect, quit, input, and emergency recovery remain;
- no launch URI, intent launcher, protocol router, native session, or alternate media client is constructed.

Use a constructor with only client id, server URL, and `BeaconService`:

```java
BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://10.0.2.2:5080", service);
model.launch(game);
assertEquals(1, service.launchCalls);
assertEquals("launch body", model.latestStream());
```

Run `BeaconViewModelTest` and `BeaconViewModelSessionTest`. Expected: FAIL until dependencies are removed.

- [ ] **Step 2: Remove media handoff from ViewModel and session lifecycle**

Delete `StreamConnectionLauncher`, `NativeStreamClient`, `NativeStreamPresentation`, `nativeStreamActive`, parsing, routing, start, and native-stop behavior from `BeaconViewModel`.

`launch()` becomes:

```java
public void launch(BeaconApiClient.GameSelection game) throws IOException {
    BeaconApiClient.BeaconResult result = service.launch(game);
    record("launch", result);
    latestStream = result.body();
}
```

`stopStream()` still calls Beacon Service and records the response. `BeaconViewModelSession.close()` no longer owns a second native client cleanup path.

- [ ] **Step 3: Remove media UI and factories from Activity**

Preserve registration/control actions, game selector, local settings, capability/telemetry reporting, input surface, and status. Remove Moonlight availability text, test pattern, native presentation switching, intent launcher, native client factory, RTSP/RTP factories, and Surface stream wiring.

The UI must show an explicit recovery status when launch succeeds but no StreamCore exists:

```text
StreamWorker recovery in progress; control-plane launch completed without media.
```

This is a temporary truthful diagnostic, not a fallback.

- [ ] **Step 4: Delete protocol and alternate-route families**

Delete production and test files matching the inventory families. Retain only the generic MediaCodec/capability classes explicitly classified `adapt` and any direct tests that do not mention HTTP samples, GameStream, Moonlight, RTSP/RTP, test protocols, launch URIs, or routers.

Run:

```powershell
rg -l -i 'GameStream|Moonlight|RTSP|RTP|nativeSession|launchUri|ACTION_VIEW|beacon-test' `
  src/Beacon.Android/app/src --glob '*.{java,cpp,h,kt,xml,gradle}'
```

Expected after deletion: no matches.

- [ ] **Step 5: Remove native compatibility module and submodules**

Remove `:streaming-moonlight` from `settings.gradle`, remove the app project dependency, remove both `.gitmodules` entries, and remove the tracked submodule paths. Remove the Moonlight instrumentation test.

Do not retain copied submodule trees elsewhere.

- [ ] **Step 6: Simplify Android CI**

Change checkout to normal checkout without recursive submodules. Remove NDK and CMake packages while retaining Android platform/build-tools setup. Build with:

```yaml
- run: sdkmanager "platforms;android-35" "build-tools;35.0.0"
- run: gradle -p src/Beacon.Android test assembleDebug
```

- [ ] **Step 7: Remove Android from the temporary debt allowlist**

Delete the Android baseline compatibility set and `streaming-moonlight` root from `ArchitectureRecoveryBoundaryTests`. Add a final assertion that no forbidden compatibility filename or token exists under Android production/test sources.

- [ ] **Step 8: Validate Android and emulator control plane**

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src/Beacon.Android test assembleDebug
adb -s emulator-5554 install -r src/Beacon.Android/app/build/outputs/apk/debug/app-debug.apk
adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity
```

Verify the APK opens, loads the server catalog, selects a game, preserves local settings, and reports the explicit no-media recovery state without crashing or launching another app.

- [ ] **Step 9: Commit and sync Checkpoint C**

```powershell
git add .github/workflows/ci.yml .gitmodules src/Beacon.Android tests/Beacon.Core.Tests/Architecture
git commit -m "refactor: collapse Android to one Beacon boundary"
git push
```

## Task 6: Remove Obsolete Contracts And Rewrite Current Documentation

**Files:**

- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Delete: `docs/external-streaming-wrapper-manifest.md`
- Delete: `docs/examples/external-streaming-runtime-session.example.json`
- Delete: `docs/source-audits/2026-07-10-moonlight-native-core.md`
- Delete: historical compatibility plans identified by the inventory
- Modify: `tests/Beacon.Core.Tests/Docs/ReadmeLinkTests.cs` if link coverage needs expansion
- Modify: `tests/Beacon.Core.Tests/Architecture/ArchitectureRecoveryBoundaryTests.cs`

- [ ] **Step 1: Rewrite README around current reality**

The README must contain:

- product and architecture summary;
- current recovery state;
- preserved projects and their purpose;
- build/test commands;
- Client Lab, fake endpoint, display probe, game probe, Cockpit, server, and APK commands;
- an explicit statement that Windows media streaming intentionally fails closed until StreamWorker lands;
- links to the authoritative specification, inventory, and this plan.

Remove the long historical milestone narrative and every instruction for wrappers, GameStream, Moonlight, RTSP/RTP, descriptor files, launch URIs, or backend selection.

- [ ] **Step 2: Update extraction map**

Record each deleted upstream compatibility path as `deleted during architecture recovery`. Keep provenance entries for display, game discovery, recovery, input, and generic decoder primitives. Do not claim a StreamWorker source decision before Gate 3 audit.

- [ ] **Step 3: Delete obsolete contracts and plans**

Delete the three named compatibility documents and every milestone plan whose primary deliverable is:

- external wrapper/process integration;
- wrapper manifest/descriptor files;
- Sunshine endpoint compatibility;
- GameStream/Moonlight/RTSP/RTP transport;
- Android launch URI/intent fallback;
- alternate diagnostic streaming protocols.

Keep plans whose primary deliverable is display lifecycle, game collection, recovery, ownership, capability/telemetry facts, local settings, catalog selection, or generic input.

- [ ] **Step 4: Convert the architecture guard from allowlist to prohibition**

Remove all temporary allowed debt. Assert the protected boundaries contain none of:

```text
Moonlight
GameStream
RTSP
RTP
ExternalProcessStreaming
StreamingWrapper
WrapperChild
RuntimeDescriptor
LaunchUri
nativeSession
```

Allow these names only in the authoritative specification, recovery inventory, extraction-map provenance, and Git history.

- [ ] **Step 5: Run documentation and boundary tests**

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter "ReadmeLinkTests|ArchitectureRecoveryBoundaryTests"
rg -n -i 'external-process|streaming wrapper|runtime descriptor|Moonlight|GameStream|RTSP|RTP|launchUri|nativeSession' `
  src tests README.md --glob '!docs/**'
```

Expected: tests pass; search results are empty except source-provenance comments explicitly approved by the inventory.

- [ ] **Step 6: Commit documentation cleanup**

```powershell
git add README.md docs tests/Beacon.Core.Tests
git commit -m "docs: remove obsolete streaming architecture"
git push
```

## Task 7: Full Static And Dynamic Validation

**Files:** no production edits unless a failing test demonstrates a defect.

- [ ] **Step 1: Run .NET validation**

```powershell
dotnet restore Beacon.slnx
dotnet format Beacon.slnx --verify-no-changes --no-restore
dotnet build Beacon.slnx -warnaserror --no-restore
dotnet test Beacon.slnx --no-build
```

- [ ] **Step 2: Run Android validation**

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src/Beacon.Android test assembleDebug
adb -s emulator-5554 install -r src/Beacon.Android/app/build/outputs/apk/debug/app-debug.apk
adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity
```

Verify catalog selection, local settings, capability/telemetry reporting, input control request, stop/disconnect/quit, and emergency restore remain usable. Verify no media route or external app is launched.

- [ ] **Step 3: Run Client Lab and browser validation**

```powershell
pnpm --dir src/Beacon.ClientLab install --frozen-lockfile
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright install --frozen-lockfile
pnpm --dir tests/Beacon.ClientLab.Playwright lint
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

- [ ] **Step 4: Run fake endpoint and probes**

Run the fake endpoint's no-phone script against fake host mode. Run `Beacon.DisplayProbe` read-only status and `Beacon.GameProbe` scan. Do not run a topology-changing display command in automated validation.

- [ ] **Step 5: Run final static absence checks**

```powershell
git diff --check
git status --short
git submodule status
rg -n -i 'external-process|streaming wrapper|runtime descriptor|Moonlight|GameStream|RTSP|RTP|launchUri|nativeSession' src tests
```

Expected: clean diff check; no submodules; no compatibility matches in runtime/test code.

- [ ] **Step 6: Commit any evidence-backed fixes**

Add a regression test before each fix. Do not add compatibility shims to satisfy old tests.

## Task 8: First Sync, Refactor Audit, Second Sync

- [ ] **Step 1: Open the first recovery PR**

Push the branch, open a ready PR, wait for all duplicate CI jobs, and merge only when `.NET`, Android, and Client Lab jobs pass.

- [ ] **Step 2: Return to synchronized main and create a refactor branch**

Audit only:

- duplicate protocol-neutral health/session mapping;
- stale compatibility names or settings;
- dead Android classes left after route deletion;
- tests that preserve removed behavior;
- accidental fake streaming registration in Windows mode;
- source/provenance documentation drift.

- [ ] **Step 3: Add regressions before justified refactors**

Do not rename or abstract preserved control-plane code unless the audit finds an actual boundary violation or duplication created by the deletion.

- [ ] **Step 4: Repeat the complete static/dynamic matrix**

Run every command from Task 7 again, including emulator and browser checks.

- [ ] **Step 5: Open and merge the second recovery PR**

Wait for all CI jobs, merge, return to clean synchronized `main`, and record the final Gates 0–2 evidence in the recovery inventory.

## Task 9: Gate 3 Handoff

After Gates 0–2 are merged, create a new source-audit and implementation plan from the cleaned repository. That plan must make exact source decisions for:

- Windows capture;
- GPU conversion and scaling;
- H.264 encoder first;
- audio capture/encode;
- media packetization, recovery, and congestion behavior;
- authenticated transport;
- Android decoder/render integration;
- input transport and injection;
- Service-to-StreamWorker IPC;
- Worker-to-StreamCore session contract.

The Gate 3 plan cannot restore Apollo, Sunshine, GameStream, Moonlight, wrapper, descriptor-file, launch-URI, or alternate Android compatibility. It must produce one Beacon-owned fake transport proof before real capture and one real H.264 emulator vertical slice before HEVC, AV1, audio, HDR, or UI refinement.

## Exit Evidence

- Core is protocol-neutral.
- External wrapper/process/probe code is absent.
- Production configuration exposes no streaming backend choice.
- Server APIs and Cockpit expose only Beacon-owned generic state.
- Android contains no compatibility or alternate media path and no streaming submodule.
- Fake-host control-plane behavior, display lifecycle, games, ownership, recovery, local settings, capability facts, telemetry facts, and generic decoder primitives remain tested.
- Windows host media fails closed rather than pretending a stream exists.
- CI, emulator control-plane checks, Client Lab, Playwright, and static absence checks pass twice around the refactor sync.
- The repository is ready for a source-evidence-backed StreamWorker/StreamCore plan.
