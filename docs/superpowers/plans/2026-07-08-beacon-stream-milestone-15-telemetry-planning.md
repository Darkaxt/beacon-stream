# Telemetry Planning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Beacon's initial stream plan visibly depend on client capabilities, profile preferences, and fakeable live telemetry profiles before display, launch, or stream side effects.

**Architecture:** Keep planning in `Beacon.Core.Sessions.SessionPlanner`; extend client fact records without creating a native networking loop. Server endpoints keep accepting JSON capabilities and telemetry, while Client Lab and FakeEndpoint gain named telemetry profiles so no-phone validation can prove different codec, FPS, bitrate, transport, and reason choices.

**Tech Stack:** .NET 10 records and xUnit for core/server/fake endpoint tests; TypeScript/Vite/Vitest/Playwright for Client Lab simulation.

---

## Requirement Slice

- `REQ-NET-001`: estimate endpoint capability, endpoint load, and network quality before launch when facts are available.
- `REQ-NET-002`: accept live telemetry fields for RTT, packet loss, Wi-Fi band, decoder load, battery, thermal state, and estimated bandwidth.
- `REQ-NET-003`: choose codec, FPS, bitrate, transport, and congestion policy from profile plus telemetry.
- `REQ-NET-004`: prove the planner with fake telemetry profiles.
- `REQ-NET-005`: keep `120 FPS` explicit when requested and supported.
- `REQ-TEST-001` through `REQ-TEST-005`: validate without the real phone through Client Lab and Playwright.

## Files

- Modify: `src/Beacon.Core/Clients/EndpointCapabilities.cs`
  - Add optional capability facts after existing constructor fields: `MaxFps`, `LowLatencyDecode`, and `CurrentScreenMode`.
- Modify: `src/Beacon.Core/Clients/TelemetrySnapshot.cs`
  - Add optional telemetry facts: `EstimatedBandwidthMbps`, `WifiBand`, `BatteryPercent`, and `ThermalState`.
- Modify: `src/Beacon.Core/Sessions/SessionPlan.cs`
  - Add stream explanation fields with optional constructor defaults so existing tests remain mechanically simple.
- Modify: `src/Beacon.Core/Sessions/SessionPlanner.cs`
  - Centralize codec/FPS/bitrate/transport selection and produce a human-readable stream reason.
- Modify: `tests/Beacon.Core.Tests/Sessions/SessionPlannerTests.cs`
  - Add red tests for excellent LAN, high RTT, packet loss, low bitrate cap, thermal/battery constrained, and explicit codec preference behavior.
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
  - Assert `/clients/{id}/plan` returns stream reason and respects telemetry/profile facts.
- Modify: `src/Beacon.FakeEndpoint/FakeEndpointRunner.cs`
  - Add named telemetry profiles and send their full telemetry/capability payloads.
- Modify: `src/Beacon.FakeEndpoint/FakeEndpointCommandLine.cs`
  - Add `--telemetry-profile` plus targeted override flags.
- Modify: `tests/Beacon.FakeEndpoint.Tests/FakeEndpointRunnerTests.cs`
  - Assert profile parsing and request JSON contain telemetry profile facts.
- Modify: `src/Beacon.ClientLab/src/clientLab.ts`
  - Add telemetry profile types, profile factory, request body builders, and plan formatting.
- Modify: `src/Beacon.ClientLab/src/main.ts`
  - Add telemetry profile selection, submit capabilities/telemetry before plan, and log stream reason.
- Modify: `src/Beacon.ClientLab/index.html`
  - Add a compact telemetry selector and summary in the existing Client Profile panel.
- Modify: `src/Beacon.ClientLab/src/clientLab.test.ts`
  - Add Vitest coverage for telemetry profile payloads and launch/plan formatting.
- Modify: `tests/Beacon.ClientLab.Playwright/tests/client-lab.spec.ts`
  - Assert the web simulator posts capabilities/telemetry and renders the selected plan reason.
- Modify: `README.md`
  - Document fake telemetry profiles and the no-phone validation purpose.

## Task 1: Core Planning Contract

- [ ] **Step 1: Write failing core planner tests**

Add tests to `tests/Beacon.Core.Tests/Sessions/SessionPlannerTests.cs`:

```csharp
[Fact]
public void ExcellentLanKeepsRequested120FpsAndExplainsPlan()
{
    SessionPlanResult result = SessionPlanner.CreatePlan(
        ClientProfile.CreateZFold7Default(),
        new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: true, VirtualDisplayHdrSupported: false, MaxFps: 120),
        new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: 20, EstimatedBandwidthMbps: 120, WifiBand: "wifi-7"),
        Dispatch);

    SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
    Assert.Equal(120, plan.Stream.Fps);
    Assert.Equal(65, plan.Stream.InitialBitrateMbps);
    Assert.Equal("av1", plan.Stream.Codec);
    Assert.Contains("excellent LAN", plan.Stream.Reason, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void HighRttUsesConservativeTransportAndLowerFps()
{
    SessionPlanResult result = SessionPlanner.CreatePlan(
        ClientProfile.CreateZFold7Default(),
        new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false, MaxFps: 120),
        new TelemetrySnapshot(RttMs: 115, PacketLossPercent: 0.5, DecoderLoadPercent: 35, EstimatedBandwidthMbps: 80, WifiBand: "wifi-5"),
        Dispatch);

    SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
    Assert.Equal(60, plan.Stream.Fps);
    Assert.Equal(25, plan.Stream.InitialBitrateMbps);
    Assert.Equal("latency-protect", plan.Stream.CongestionPolicy);
    Assert.Equal("lan-conservative", plan.Stream.Transport);
    Assert.Contains("RTT", plan.Stream.Reason, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void PacketLossKeepsResolutionButProtectsBitrate()
{
    SessionPlanResult result = SessionPlanner.CreatePlan(
        ClientProfile.CreateZFold7Default(),
        new EndpointCapabilities(Av1: false, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false, MaxFps: 120),
        new TelemetrySnapshot(RttMs: 22, PacketLossPercent: 3.2, DecoderLoadPercent: 40, EstimatedBandwidthMbps: 90, WifiBand: "wifi-6"),
        Dispatch);

    SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
    Assert.Equal(2560, plan.Display.Width);
    Assert.Equal(1600, plan.Display.Height);
    Assert.Equal("hevc", plan.Stream.Codec);
    Assert.Equal(35, plan.Stream.InitialBitrateMbps);
    Assert.Equal("loss-protect", plan.Stream.CongestionPolicy);
}

[Fact]
public void BitrateCapWinsOverExcellentNetwork()
{
    ClientProfile profile = ClientProfile.CreateZFold7Default() with
    {
        Stream = ClientProfile.CreateZFold7Default().Stream with { BitrateCapMbps = 40 }
    };

    SessionPlanResult result = SessionPlanner.CreatePlan(
        profile,
        new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false, MaxFps: 120),
        new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: 20, EstimatedBandwidthMbps: 200, WifiBand: "wifi-7"),
        Dispatch);

    SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
    Assert.Equal(40, plan.Stream.InitialBitrateMbps);
    Assert.Contains("bitrate cap", plan.Stream.Reason, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void ThermalAndBatteryConstrainedEndpointUsesPowerSave()
{
    SessionPlanResult result = SessionPlanner.CreatePlan(
        ClientProfile.CreateZFold7Default(),
        new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false, MaxFps: 120),
        new TelemetrySnapshot(RttMs: 12, PacketLossPercent: 0, DecoderLoadPercent: 88, EstimatedBandwidthMbps: 100, WifiBand: "wifi-6", BatteryPercent: 9, ThermalState: "hot"),
        Dispatch);

    SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
    Assert.Equal(60, plan.Stream.Fps);
    Assert.Equal(30, plan.Stream.InitialBitrateMbps);
    Assert.Equal("power-save", plan.Stream.CongestionPolicy);
    Assert.Contains("thermal", plan.Stream.Reason, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void ExplicitHevcPreferenceOverridesAutoAv1WhenAvailable()
{
    ClientProfile profile = ClientProfile.CreateZFold7Default() with
    {
        Stream = ClientProfile.CreateZFold7Default().Stream with { CodecPreference = "hevc" }
    };

    SessionPlanResult result = SessionPlanner.CreatePlan(
        profile,
        new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: false, VirtualDisplayHdrSupported: false, MaxFps: 120),
        new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: 20, EstimatedBandwidthMbps: 120, WifiBand: "wifi-7"),
        Dispatch);

    SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
    Assert.Equal("hevc", plan.Stream.Codec);
    Assert.Contains("profile", plan.Stream.Reason, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Verify tests fail**

Run:

```powershell
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter SessionPlannerTests
```

Expected: compile failures for new constructor properties or assertion failures for unchanged planner decisions.

- [ ] **Step 3: Implement records and planner**

Implementation rules:

- Keep new record fields optional and append them after existing fields.
- Preserve `2560x1600` display geometry regardless of telemetry.
- Preserve `120 FPS` on excellent LAN when profile and capability support it.
- Lower FPS only for high RTT, high decoder load, thermal/battery constrained, or endpoint `MaxFps` below profile refresh.
- Apply bitrate cap last.
- Add a concise `Reason` string to `PlannedStream`.

- [ ] **Step 4: Verify core tests pass**

Run:

```powershell
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter SessionPlannerTests
```

Expected: all SessionPlanner tests pass.

## Task 2: Server Plan Response Visibility

- [ ] **Step 1: Write failing server tests**

Modify `tests/Beacon.Server.Tests/ClientApiTests.cs` so `CapabilitiesAndTelemetryInfluencePlanWithoutChangingDisplayGeometry` posts the expanded telemetry payload and asserts:

```csharp
Assert.Equal("lan-conservative", root.GetProperty("stream").GetProperty("transport").GetString());
Assert.Equal("latency-protect", root.GetProperty("stream").GetProperty("congestionPolicy").GetString());
Assert.Contains("RTT", root.GetProperty("stream").GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
```

Add a new test where profile patch sets `bitrateCapMbps = 40`, excellent telemetry is posted, and `/plan` returns `initialBitrateMbps = 40` plus a reason containing `bitrate cap`.

- [ ] **Step 2: Verify server tests fail before implementation**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "CapabilitiesAndTelemetryInfluencePlanWithoutChangingDisplayGeometry|PlanHonorsClientBitrateCap"
```

Expected: missing or incorrect `stream.reason`, `transport`, or cap behavior.

- [ ] **Step 3: Let the existing JSON response expose the richer stream record**

No custom serializer is needed if `PlannedStream` owns the new properties. Keep `/clients/{id}/plan` and `/clients/{id}/launch` response shapes aligned by returning the same stream object.

- [ ] **Step 4: Verify server tests pass**

Run the same filtered server command. Expected: pass.

## Task 3: Fake Endpoint Telemetry Profiles

- [ ] **Step 1: Write failing fake endpoint tests**

Extend `tests/Beacon.FakeEndpoint.Tests/FakeEndpointRunnerTests.cs`:

```csharp
[Fact]
public void ParsesTelemetryProfileAndTelemetryOverridesFromCommandLine()
{
    FakeEndpointCommandLineOptions options = FakeEndpointCommandLine.Parse(
        [
            "--telemetry-profile", "thermal-battery",
            "--rtt-ms", "44",
            "--packet-loss", "1.25",
            "--estimated-bandwidth", "70",
            "--wifi-band", "wifi-6"
        ]);

    Assert.Equal("thermal-battery", options.Script.TelemetryProfile);
    Assert.Equal(44, options.Script.RttMs);
    Assert.Equal(1.25, options.Script.PacketLossPercent);
    Assert.Equal(70, options.Script.EstimatedBandwidthMbps);
    Assert.Equal("wifi-6", options.Script.WifiBand);
}

[Fact]
public async Task SendsExpandedTelemetryFacts()
{
    var handler = new RecordingHandler();
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    var runner = new FakeEndpointRunner(client);
    FakeEndpointScript script = FakeEndpointScript.CreateZFold7Default().ApplyTelemetryProfile("packet-loss");

    FakeEndpointResult result = await runner.RunAsync(script, CancellationToken.None);

    Assert.True(result.Success);
    string telemetryBody = handler.Bodies[4];
    Assert.Contains("\"packetLossPercent\":3.2", telemetryBody, StringComparison.Ordinal);
    Assert.Contains("\"wifiBand\":\"wifi-6\"", telemetryBody, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Verify fake endpoint tests fail**

Run:

```powershell
dotnet test tests\Beacon.FakeEndpoint.Tests\Beacon.FakeEndpoint.Tests.csproj
```

Expected: compile failures for missing script fields/profile method and CLI flags.

- [ ] **Step 3: Implement named profiles**

Add these profile names and facts:

| Profile | RTT | Loss | Bandwidth | Wi-Fi | Decoder | Battery | Thermal |
| --- | ---: | ---: | ---: | --- | ---: | ---: | --- |
| `excellent-lan` | 8 | 0 | 120 | `wifi-7` | 20 | 80 | `nominal` |
| `congested-lan` | 55 | 1.5 | 45 | `wifi-6` | 55 | 60 | `nominal` |
| `high-rtt` | 115 | 0.5 | 80 | `wifi-5` | 35 | 70 | `nominal` |
| `packet-loss` | 22 | 3.2 | 90 | `wifi-6` | 40 | 70 | `nominal` |
| `low-bitrate-cap` | 8 | 0 | 35 | `wifi-6` | 30 | 75 | `nominal` |
| `thermal-battery` | 12 | 0 | 100 | `wifi-6` | 88 | 9 | `hot` |

- [ ] **Step 4: Verify fake endpoint tests pass**

Run:

```powershell
dotnet test tests\Beacon.FakeEndpoint.Tests\Beacon.FakeEndpoint.Tests.csproj
```

Expected: pass.

## Task 4: Client Lab Telemetry Simulation

- [ ] **Step 1: Write failing TypeScript tests**

Add Vitest assertions in `src/Beacon.ClientLab/src/clientLab.test.ts`:

```ts
it('builds telemetry payloads from named profiles', () => {
  const payload = createTelemetryPayload('thermal-battery');

  expect(payload).toMatchObject({
    rttMs: 12,
    packetLossPercent: 0,
    decoderLoadPercent: 88,
    estimatedBandwidthMbps: 100,
    wifiBand: 'wifi-6',
    batteryPercent: 9,
    thermalState: 'hot'
  });
});

it('formats plan details with reason', () => {
  expect(formatPlanDetails({
    display: { mode: 'virtual-primary', width: 2560, height: 1600, refreshHz: 120 },
    stream: { codec: 'av1', fps: 120, initialBitrateMbps: 65, transport: 'lan-direct', congestionPolicy: 'adaptive', reason: 'Excellent LAN telemetry kept 120 FPS.' }
  })).toContain('Excellent LAN');
});
```

- [ ] **Step 2: Verify Client Lab tests fail**

Run:

```powershell
pnpm --dir src\Beacon.ClientLab test
```

Expected: missing exported functions/types.

- [ ] **Step 3: Implement Client Lab telemetry UI and request flow**

Add:

- `<select id="telemetryProfileInput">` with the six profile names.
- A submit action before plan and launch:
  - `POST /clients/z-fold-7/capabilities`
  - `POST /clients/z-fold-7/telemetry`
- Plan log line including codec, FPS, bitrate, transport, congestion policy, and reason.

- [ ] **Step 4: Update Playwright route assertions**

Modify `tests/Beacon.ClientLab.Playwright/tests/client-lab.spec.ts` so it expects:

- `POST /clients/z-fold-7/capabilities`
- `POST /clients/z-fold-7/telemetry`
- plan response `stream.reason`
- visible reason text after clicking `Plan`

- [ ] **Step 5: Verify Client Lab checks pass**

Run:

```powershell
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright lint
pnpm --dir tests\Beacon.ClientLab.Playwright test
```

Expected: all pass.

## Task 5: Documentation, Validation, And Sync

- [ ] **Step 1: Update README**

Document:

- Fake telemetry profiles.
- `fake-endpoint --telemetry-profile excellent-lan`.
- Planner choices are initial recommendations, not live adaptation.

- [ ] **Step 2: Run static and dynamic validation**

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

Expected:

- All build/test/lint commands pass.
- The final `rg` exits with no matches.
- Android may still report the existing Gradle 9 deprecation warning while returning success.

- [ ] **Step 3: Commit and sync**

Run:

```powershell
git add README.md src tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-15-telemetry-planning.md
git commit -m "Add telemetry-driven initial planning"
git push -u origin codex/milestone-15-telemetry-planning
gh pr create --draft --base main --head codex/milestone-15-telemetry-planning --title "Add telemetry-driven initial planning" --body "Milestone 15 telemetry planning implementation."
gh pr checks <pr-number> --watch
gh pr ready <pr-number>
gh pr merge <pr-number> --merge --delete-branch
```

Expected: PR checks pass, PR merges, local `main` is clean and matches `origin/main`.

## Non-Goals

- No real-time adaptive bitrate loop.
- No native Sunshine/Moonlight protocol work.
- No phone-only telemetry dependency.
- No display topology changes.
- No new global settings page.

## Self-Review

- Spec coverage: covers the network and no-phone testing gaps that remain after Milestone 14.
- Placeholder scan: no TBD or open-ended implementation steps remain.
- Type consistency: new optional fields are appended to existing records so current call sites can migrate incrementally.
- Scope check: this is one milestone focused on initial plan selection and simulator visibility.
