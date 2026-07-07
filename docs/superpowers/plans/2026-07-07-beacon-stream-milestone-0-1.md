# Beacon Stream Milestone 0/1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the remote-first Beacon Stream Milestone 0/1 foundation: source-boundary docs, .NET 10 solution, server-authoritative core contracts, fake backends, fake endpoint, Client Lab, and tests proving profile/planner/lifecycle/recovery policy without a phone.

**Architecture:** Keep policy in `Beacon.Core`, transport/API in `Beacon.Server`, scripted remote-client simulation in `Beacon.FakeEndpoint`, and interactive browser simulation in `Beacon.ClientLab`. Real Windows display, real streaming, real Android, and copied upstream source are out of scope for this plan.

**Tech Stack:** .NET 10, C# 14, ASP.NET Core minimal API, xUnit, TypeScript/Vite for Client Lab, Playwright for browser checks, GitHub Actions, GPL-3.0.

---

## Scope Guard

This plan implements Milestone 0 and Milestone 1 from `docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md`.

In scope:

- Public `Darkaxt/beacon-stream` repo created with `gh`.
- GPL-3.0 baseline.
- Source extraction map and license notes.
- Core contracts for profiles, capabilities, telemetry, plans, displays, sessions, games, and recovery.
- Fake display, stream, process/window, game-library, and telemetry backends.
- Session planner and lifecycle policy tests.
- Minimal server API around fake backends.
- CLI fake endpoint.
- Client Lab web simulator.
- Static and dynamic validation gates.
- Remote sync checkpoints after implementation and refactor passes.

Out of scope:

- Real streaming backend.
- Real SudoVDA/Windows display backend.
- Real Android APK.
- Copying Sunshine/Apollo/Vibeshine/Vibepollo source before the extraction map names the boundary.
- Per-game display overrides.
- HDR success guarantees beyond truthful planning/reporting.

## File Structure

Create or modify these files:

- `global.json` pins .NET SDK `10.0.301`.
- `.editorconfig` defines C# formatting and nullable conventions.
- `Directory.Build.props` enables nullable, implicit usings, deterministic builds, and warnings-as-errors for repo code.
- `Beacon.slnx` contains all .NET projects.
- `src/Beacon.Core/Beacon.Core.csproj` contains server-authoritative domain and policy.
- `src/Beacon.Core/Clients/*` contains client identity, profile, patch allowlist, capabilities, and telemetry.
- `src/Beacon.Core/Sessions/*` contains session planning and session lifecycle models.
- `src/Beacon.Core/Displays/*` contains display modes, HDR models, lease policy, and fake display contracts.
- `src/Beacon.Core/Games/*` contains normalized game models and provider interfaces.
- `src/Beacon.Core/Recovery/*` contains recovery command models and result types.
- `src/Beacon.Server/Beacon.Server.csproj` hosts the minimal API.
- `src/Beacon.Server/Program.cs` wires in-memory stores and fake backends.
- `src/Beacon.FakeEndpoint/Beacon.FakeEndpoint.csproj` provides scripted phone-free client flows.
- `src/Beacon.ClientLab/package.json` defines the browser simulator scripts.
- `src/Beacon.ClientLab/src/*` contains TypeScript UI/control-plane client code.
- `tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj` tests profile, planner, display lifecycle, and recovery policy.
- `tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj` tests API behavior with fake backends.
- `tests/Beacon.FakeEndpoint.Tests/Beacon.FakeEndpoint.Tests.csproj` tests scripted client flows.
- `tests/Beacon.ClientLab.Playwright/package.json` and `tests/Beacon.ClientLab.Playwright/tests/*.spec.ts` test Client Lab in a browser.
- `docs/extraction-map.md` records source reuse decisions.
- `docs/license-notes.md` records GPL and upstream boundary notes.
- `.github/workflows/ci.yml` runs static and dynamic validation.

## Sync Contract

Every implementation stage follows this loop:

```text
Implement
Validate static and dynamic checks
Sync
Refactor
Validate static and dynamic checks
Sync
```

Sync means:

```powershell
git status --short
git add <coherent files>
git commit -m "<checkpoint message>"
git push -u origin codex/milestone-0-1-scaffold
```

No substantial work may remain local-only across a context compaction.

## Validation Commands

Static validation:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir tests/Beacon.ClientLab.Playwright lint
```

Dynamic validation:

```powershell
dotnet test Beacon.slnx
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Until Client Lab exists, skip only the pnpm commands and record that skip in the checkpoint message.

---

### Task 1: Remote-First Baseline And Solution Scaffold

**Files:**
- Modify: `README.md`
- Create: `global.json`
- Create: `.editorconfig`
- Create: `Directory.Build.props`
- Create: `Beacon.slnx`
- Create: `src/Beacon.Core/Beacon.Core.csproj`
- Create: `src/Beacon.Server/Beacon.Server.csproj`
- Create: `src/Beacon.FakeEndpoint/Beacon.FakeEndpoint.csproj`
- Create: `tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj`
- Create: `tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj`
- Create: `tests/Beacon.FakeEndpoint.Tests/Beacon.FakeEndpoint.Tests.csproj`

- [ ] **Step 1: Verify remote-first state**

Run:

```powershell
gh repo view Darkaxt/beacon-stream --json name,url,visibility,defaultBranchRef
git remote -v
git status --short --branch
```

Expected:

```text
visibility: PUBLIC
origin points to https://github.com/Darkaxt/beacon-stream.git
branch is codex/milestone-0-1-scaffold
working tree contains only planned docs/scaffold edits
```

- [ ] **Step 2: Create .NET 10 solution and projects**

Run:

```powershell
dotnet new globaljson --sdk-version 10.0.301 --force
dotnet new sln --name Beacon
dotnet new classlib --framework net10.0 --name Beacon.Core --output src/Beacon.Core
dotnet new web --framework net10.0 --name Beacon.Server --output src/Beacon.Server
dotnet new console --framework net10.0 --name Beacon.FakeEndpoint --output src/Beacon.FakeEndpoint
dotnet new xunit --framework net10.0 --name Beacon.Core.Tests --output tests/Beacon.Core.Tests
dotnet new xunit --framework net10.0 --name Beacon.Server.Tests --output tests/Beacon.Server.Tests
dotnet new xunit --framework net10.0 --name Beacon.FakeEndpoint.Tests --output tests/Beacon.FakeEndpoint.Tests
dotnet sln Beacon.slnx add src/Beacon.Core/Beacon.Core.csproj
dotnet sln Beacon.slnx add src/Beacon.Server/Beacon.Server.csproj
dotnet sln Beacon.slnx add src/Beacon.FakeEndpoint/Beacon.FakeEndpoint.csproj
dotnet sln Beacon.slnx add tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj
dotnet sln Beacon.slnx add tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj
dotnet sln Beacon.slnx add tests/Beacon.FakeEndpoint.Tests/Beacon.FakeEndpoint.Tests.csproj
dotnet add src/Beacon.Server/Beacon.Server.csproj reference src/Beacon.Core/Beacon.Core.csproj
dotnet add src/Beacon.FakeEndpoint/Beacon.FakeEndpoint.csproj reference src/Beacon.Core/Beacon.Core.csproj
dotnet add tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj reference src/Beacon.Core/Beacon.Core.csproj
dotnet add tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj reference src/Beacon.Server/Beacon.Server.csproj
dotnet add tests/Beacon.FakeEndpoint.Tests/Beacon.FakeEndpoint.Tests.csproj reference src/Beacon.FakeEndpoint/Beacon.FakeEndpoint.csproj
```

Expected: all commands exit 0.

- [ ] **Step 3: Replace default placeholder classes**

Delete generated `Class1.cs` and `UnitTest1.cs` files before adding real tests.

Run:

```powershell
Get-ChildItem -Recurse -Filter Class1.cs
Get-ChildItem -Recurse -Filter UnitTest1.cs
```

Expected: the files exist before deletion and do not exist after deletion.

- [ ] **Step 4: Add repository build defaults**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

Create `.editorconfig`:

```ini
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true
trim_trailing_whitespace = true

[*.cs]
indent_style = space
indent_size = 4
dotnet_style_qualification_for_field = false:suggestion
dotnet_style_qualification_for_property = false:suggestion
dotnet_style_qualification_for_method = false:suggestion
dotnet_style_qualification_for_event = false:suggestion
csharp_style_namespace_declarations = file_scoped:suggestion
```

- [ ] **Step 5: Update README with scope**

Replace `README.md` with:

```markdown
# Beacon Stream

Beacon Stream is a server-authoritative personal game-streaming orchestrator.

Milestone 0/1 focuses on the control plane, fake backends, planner, profile ownership, phone-free testing, and source-boundary documentation. Real streaming, real Android, and real Windows virtual display integration come later.

See:

- `docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md`
- `docs/superpowers/plans/2026-07-07-beacon-stream-milestone-0-1.md`
```

- [ ] **Step 6: Validate scaffold**

Run:

```powershell
dotnet restore Beacon.slnx
dotnet build Beacon.slnx -warnaserror
dotnet test Beacon.slnx
```

Expected: restore, build, and tests exit 0.

- [ ] **Step 7: Sync baseline checkpoint**

Run:

```powershell
git status --short
git add .
git commit -m "Scaffold Beacon Stream solution"
git push -u origin codex/milestone-0-1-scaffold
```

Expected: commit created and branch pushed to GitHub.

### Task 2: Client Profile Contracts And APK Patch Allowlist

**Files:**
- Create: `src/Beacon.Core/Clients/ClientId.cs`
- Create: `src/Beacon.Core/Clients/ClientProfile.cs`
- Create: `src/Beacon.Core/Clients/ClientProfilePatch.cs`
- Create: `src/Beacon.Core/Clients/ClientProfilePatcher.cs`
- Create: `src/Beacon.Core/Displays/DisplayPreferences.cs`
- Create: `src/Beacon.Core/Displays/HdrPreference.cs`
- Create: `tests/Beacon.Core.Tests/Clients/ClientProfilePatcherTests.cs`

- [ ] **Step 1: Write failing profile allowlist tests**

Create `tests/Beacon.Core.Tests/Clients/ClientProfilePatcherTests.cs`:

```csharp
using Beacon.Core.Clients;
using Beacon.Core.Displays;

namespace Beacon.Core.Tests.Clients;

public sealed class ClientProfilePatcherTests
{
    [Fact]
    public void ZFold7DefaultPreserves1600p120Preference()
    {
        ClientProfile profile = ClientProfile.CreateZFold7Default();

        Assert.Equal("z-fold-7", profile.ClientId.Value);
        Assert.Equal(2560, profile.Display.PreferredWidth);
        Assert.Equal(1600, profile.Display.PreferredHeight);
        Assert.Equal(120, profile.Display.PreferredRefreshHz);
        Assert.Equal(HdrPreference.Prefer, profile.Display.HdrPreference);
    }

    [Fact]
    public void AppliesOnlyApkEditableClientPreferences()
    {
        ClientProfile profile = ClientProfile.CreateZFold7Default();
        var patch = new ClientProfilePatch(
            PreferredWidth: 1920,
            PreferredHeight: 1200,
            PreferredRefreshHz: 60,
            HdrPreference: HdrPreference.Off,
            CodecPreference: "hevc",
            QualityMode: "balanced",
            BitrateCapMbps: 45,
            AudioMode: "stereo",
            KeepAppRunningOnDisconnect: true);

        ClientProfile updated = ClientProfilePatcher.ApplyApkPatch(profile, patch);

        Assert.Equal(1920, updated.Display.PreferredWidth);
        Assert.Equal(1200, updated.Display.PreferredHeight);
        Assert.Equal(60, updated.Display.PreferredRefreshHz);
        Assert.Equal(HdrPreference.Off, updated.Display.HdrPreference);
        Assert.Equal("virtual-primary", updated.Display.Mode);
        Assert.True(updated.Display.RestorePhysicalDisplayOnEnd);
        Assert.True(updated.Display.ForbidMirrorMode);
    }

    [Fact]
    public void RejectsInvalidAspectRatioCollapseTo1440pForZFold7()
    {
        ClientProfile profile = ClientProfile.CreateZFold7Default();
        var patch = new ClientProfilePatch(
            PreferredWidth: 2560,
            PreferredHeight: 1440,
            PreferredRefreshHz: 120,
            HdrPreference: HdrPreference.Prefer,
            CodecPreference: null,
            QualityMode: null,
            BitrateCapMbps: null,
            AudioMode: null,
            KeepAppRunningOnDisconnect: null);

        var error = Assert.Throws<InvalidClientProfilePatchException>(
            () => ClientProfilePatcher.ApplyApkPatch(profile, patch));

        Assert.Contains("2560x1440", error.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter ClientProfilePatcherTests
```

Expected: FAIL because `Beacon.Core.Clients` and `Beacon.Core.Displays` types do not exist.

- [ ] **Step 3: Implement minimal client profile model**

Create `src/Beacon.Core/Clients/ClientId.cs`:

```csharp
namespace Beacon.Core.Clients;

public readonly record struct ClientId(string Value)
{
    public override string ToString() => Value;
}
```

Create `src/Beacon.Core/Displays/HdrPreference.cs`:

```csharp
namespace Beacon.Core.Displays;

public enum HdrPreference
{
    Off,
    Prefer,
    Require
}
```

Create `src/Beacon.Core/Displays/DisplayPreferences.cs`:

```csharp
namespace Beacon.Core.Displays;

public sealed record DisplayPreferences(
    int PreferredWidth,
    int PreferredHeight,
    int PreferredRefreshHz,
    HdrPreference HdrPreference,
    string Mode,
    bool RestorePhysicalDisplayOnEnd,
    bool ForbidMirrorMode);
```

Create `src/Beacon.Core/Clients/ClientProfile.cs`:

```csharp
using Beacon.Core.Displays;

namespace Beacon.Core.Clients;

public sealed record StreamPreferences(string QualityMode, string CodecPreference, int? BitrateCapMbps);

public sealed record AudioPreferences(string Mode);

public sealed record SessionPreferences(bool KeepAppRunningOnDisconnect, bool AllowEmergencyRestoreFromClient);

public sealed record ClientProfile(
    ClientId ClientId,
    string Name,
    DisplayPreferences Display,
    StreamPreferences Stream,
    AudioPreferences Audio,
    SessionPreferences Session)
{
    public static ClientProfile CreateZFold7Default() =>
        new(
            new ClientId("z-fold-7"),
            "Z Fold 7",
            new DisplayPreferences(2560, 1600, 120, HdrPreference.Prefer, "virtual-primary", true, true),
            new StreamPreferences("auto", "auto", null),
            new AudioPreferences("stereo"),
            new SessionPreferences(false, true));
}
```

Create `src/Beacon.Core/Clients/ClientProfilePatch.cs`:

```csharp
using Beacon.Core.Displays;

namespace Beacon.Core.Clients;

public sealed record ClientProfilePatch(
    int? PreferredWidth,
    int? PreferredHeight,
    int? PreferredRefreshHz,
    HdrPreference? HdrPreference,
    string? CodecPreference,
    string? QualityMode,
    int? BitrateCapMbps,
    string? AudioMode,
    bool? KeepAppRunningOnDisconnect);
```

Create `src/Beacon.Core/Clients/ClientProfilePatcher.cs`:

```csharp
using Beacon.Core.Displays;

namespace Beacon.Core.Clients;

public sealed class InvalidClientProfilePatchException(string message) : InvalidOperationException(message);

public static class ClientProfilePatcher
{
    public static ClientProfile ApplyApkPatch(ClientProfile profile, ClientProfilePatch patch)
    {
        int width = patch.PreferredWidth ?? profile.Display.PreferredWidth;
        int height = patch.PreferredHeight ?? profile.Display.PreferredHeight;
        int refresh = patch.PreferredRefreshHz ?? profile.Display.PreferredRefreshHz;

        if (profile.ClientId.Value == "z-fold-7" && width == 2560 && height == 1440)
        {
            throw new InvalidClientProfilePatchException("Z Fold 7 profile must not collapse 2560x1600 intent to 2560x1440.");
        }

        var display = profile.Display with
        {
            PreferredWidth = width,
            PreferredHeight = height,
            PreferredRefreshHz = refresh,
            HdrPreference = patch.HdrPreference ?? profile.Display.HdrPreference
        };

        var stream = profile.Stream with
        {
            CodecPreference = patch.CodecPreference ?? profile.Stream.CodecPreference,
            QualityMode = patch.QualityMode ?? profile.Stream.QualityMode,
            BitrateCapMbps = patch.BitrateCapMbps ?? profile.Stream.BitrateCapMbps
        };

        var audio = profile.Audio with
        {
            Mode = patch.AudioMode ?? profile.Audio.Mode
        };

        var session = profile.Session with
        {
            KeepAppRunningOnDisconnect = patch.KeepAppRunningOnDisconnect ?? profile.Session.KeepAppRunningOnDisconnect
        };

        return profile with { Display = display, Stream = stream, Audio = audio, Session = session };
    }
}
```

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter ClientProfilePatcherTests
```

Expected: PASS.

### Task 3: Session Planner With HDR Truthfulness And 120 FPS Decisions

**Files:**
- Create: `src/Beacon.Core/Clients/EndpointCapabilities.cs`
- Create: `src/Beacon.Core/Clients/TelemetrySnapshot.cs`
- Create: `src/Beacon.Core/Games/GameDescriptor.cs`
- Create: `src/Beacon.Core/Sessions/SessionPlan.cs`
- Create: `src/Beacon.Core/Sessions/SessionPlanner.cs`
- Create: `tests/Beacon.Core.Tests/Sessions/SessionPlannerTests.cs`

- [ ] **Step 1: Write failing planner tests**

Create `tests/Beacon.Core.Tests/Sessions/SessionPlannerTests.cs` with tests for:

```csharp
[Fact]
public void PreservesZFoldResolutionRefreshAndVirtualPrimaryMode()
```

Assert that a Z Fold 7 profile produces `2560x1600`, `120`, `virtual-primary`, and `fps` `120`.

```csharp
[Fact]
public void HdrPreferFallsBackToSdrWithReasonWhenDisplayCannotExposeHdr()
```

Assert that `HdrPreference.Prefer` with `VirtualDisplayHdrSupported = false` yields `HdrEnabled == false` and reason contains `virtual display`.

```csharp
[Fact]
public void HdrRequireFailsBeforeLaunchWhenHdrChainIsIncomplete()
```

Assert that `HdrPreference.Require` returns a failed plan result and never produces a launchable plan.

```csharp
[Fact]
public void CongestedTelemetryLowersInitialBitrateButDoesNotChangeDisplayResolution()
```

Assert high packet loss lowers bitrate and keeps `2560x1600`.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter SessionPlannerTests
```

Expected: FAIL because planner types do not exist.

- [ ] **Step 3: Implement minimal planner**

Define immutable records:

```csharp
public sealed record EndpointCapabilities(bool Av1, bool Hevc, bool H264, bool Hdr10, bool VirtualDisplayHdrSupported);
public sealed record TelemetrySnapshot(int RttMs, double PacketLossPercent, int? DecoderLoadPercent);
public sealed record GameDescriptor(string Id, string Title, string Source);
public sealed record PlannedDisplay(string DisplayId, int Width, int Height, int RefreshHz, string Mode, HdrPreference HdrPreference, bool HdrEnabled, string Reason);
public sealed record PlannedStream(string Codec, int Fps, int InitialBitrateMbps, string Transport, string CongestionPolicy);
public sealed record SessionPlan(string SessionId, ClientId ClientId, string AppId, PlannedDisplay Display, PlannedStream Stream);
public sealed record SessionPlanResult(bool Success, SessionPlan? Plan, string? Error);
```

Planner rules:

- Display id is `client-{clientId}`.
- Width/height/refresh come from client profile.
- FPS equals preferred refresh up to `120`.
- HDR `Off` disables HDR.
- HDR `Prefer` enables HDR only if endpoint and virtual display report HDR.
- HDR `Require` fails when endpoint or virtual display cannot do HDR.
- AV1 wins when available, then HEVC, then H264.
- Bitrate starts at `65`, drops to `35` when packet loss is at least `2.0`, and drops to `25` when RTT is at least `80`.
- Resolution never changes because of telemetry.

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter SessionPlannerTests
```

Expected: PASS.

### Task 4: Display Lease Policy And Fake Display Backend

**Files:**
- Create: `src/Beacon.Core/Displays/DisplayLease.cs`
- Create: `src/Beacon.Core/Displays/IDisplayBackend.cs`
- Create: `src/Beacon.Core/Displays/DisplayLeaseManager.cs`
- Create: `src/Beacon.Core/Displays/FakeDisplayBackend.cs`
- Create: `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs`

- [ ] **Step 1: Write failing display lifecycle tests**

Create tests proving:

```csharp
[Fact]
public async Task PreflightCreatesClientScopedLeaseBeforeLaunch()
```

Expected: fake backend records `EnsureVirtualDisplayAsync("client-z-fold-7", 2560, 1600, 120)`.

```csharp
[Fact]
public async Task DisconnectDoesNotTearDownLease()
```

Expected: after `DisconnectAsync`, fake backend has no remove call.

```csharp
[Theory]
[InlineData(true, false, false)]
[InlineData(false, true, false)]
[InlineData(false, false, true)]
public async Task CleanupKeepsLeaseUntilClientInactiveAndNoOwnedWorkRemains(...)
```

Expected: remove happens only when all three inputs are false.

```csharp
[Fact]
public async Task MissingVirtualDisplayFailsInsteadOfFallingBackToPhysicalDisplay()
```

Expected: manager result fails and fake backend records no physical-display fallback.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
```

Expected: FAIL because display lease types do not exist.

- [ ] **Step 3: Implement minimal display lease policy**

Implement interfaces:

```csharp
public interface IDisplayBackend
{
    Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(string displayId, int width, int height, int refreshHz, CancellationToken cancellationToken);
    Task RestorePhysicalPrimaryAsync(CancellationToken cancellationToken);
    Task RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken);
}
```

Implement policy:

```csharp
public bool CanRemove(bool clientActive, bool ownedProcessRunning, bool ownedWindowRemaining) =>
    !clientActive && !ownedProcessRunning && !ownedWindowRemaining;
```

Implement fake backend with call lists:

```csharp
public sealed List<string> EnsureCalls { get; } = [];
public sealed List<string> RemoveCalls { get; } = [];
public bool AllowEnsure { get; set; } = true;
```

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
```

Expected: PASS.

### Task 5: Server API Around Fake Backends

**Files:**
- Create: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Create: `src/Beacon.Server/State/InMemoryClientStore.cs`
- Create: `src/Beacon.Server/State/InMemorySessionStore.cs`
- Modify: `src/Beacon.Server/Program.cs`
- Create: `tests/Beacon.Server.Tests/ClientApiTests.cs`

- [ ] **Step 1: Write failing API tests**

Create tests using `WebApplicationFactory<Program>`:

```csharp
[Fact]
public async Task HelloReturnsZFoldProfileAndEditableFields()
```

Expected: `POST /clients/hello` returns `z-fold-7`, profile, and allowed fields.

```csharp
[Fact]
public async Task PatchRejectsGlobalOrDisplayPolicyFields()
```

Expected: `PATCH /clients/z-fold-7/profile` with `mode` fails with `400`.

```csharp
[Fact]
public async Task PlanReturnsCompletePlanBeforeLaunch()
```

Expected: `POST /clients/z-fold-7/plan` returns display, stream, recovery, and reasons.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
```

Expected: FAIL because endpoints do not exist.

- [ ] **Step 3: Implement API endpoints**

Expose these endpoints:

```text
POST /clients/hello
GET /clients/{clientId}/profile
PATCH /clients/{clientId}/profile
POST /clients/{clientId}/capabilities
POST /clients/{clientId}/telemetry
POST /clients/{clientId}/plan
POST /clients/{clientId}/launch
POST /clients/{clientId}/disconnect
POST /clients/{clientId}/quit
POST /clients/{clientId}/emergency-restore
```

Rules:

- Store only one default Z Fold 7 client in memory.
- Reject unknown global/display-policy fields in profile patches.
- Plan endpoint uses fake capabilities when no capabilities have been posted.
- Launch endpoint only uses fake display backend.
- Disconnect endpoint never removes the display by itself.

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
```

Expected: PASS.

### Task 6: CLI Fake Endpoint

**Files:**
- Modify: `src/Beacon.FakeEndpoint/Program.cs`
- Create: `src/Beacon.FakeEndpoint/FakeEndpointRunner.cs`
- Create: `tests/Beacon.FakeEndpoint.Tests/FakeEndpointRunnerTests.cs`

- [ ] **Step 1: Write failing CLI flow tests**

Create tests proving a scripted endpoint can:

```text
hello
fetch-profile
patch-profile --width 2560 --height 1600 --refresh 120
capabilities --av1 true --hevc true --hdr10 true
telemetry --rtt 8 --packet-loss 0
plan --app-id steam-shortcut:3767414131
disconnect
reconnect
quit
emergency-restore
```

Expected: runner emits ordered operations and blocks `2560x1440`.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/Beacon.FakeEndpoint.Tests/Beacon.FakeEndpoint.Tests.csproj --filter FakeEndpointRunnerTests
```

Expected: FAIL because runner does not exist.

- [ ] **Step 3: Implement runner**

Implement a runner class that accepts an `HttpClient`, a client id, and command arguments. Keep it thin: it serializes requests to server endpoints and returns a structured `FakeEndpointResult`.

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test tests/Beacon.FakeEndpoint.Tests/Beacon.FakeEndpoint.Tests.csproj --filter FakeEndpointRunnerTests
```

Expected: PASS.

### Task 7: Client Lab Web App

**Files:**
- Create: `src/Beacon.ClientLab/package.json`
- Create: `src/Beacon.ClientLab/index.html`
- Create: `src/Beacon.ClientLab/src/main.ts`
- Create: `src/Beacon.ClientLab/src/clientLab.ts`
- Create: `tests/Beacon.ClientLab.Playwright/package.json`
- Create: `tests/Beacon.ClientLab.Playwright/playwright.config.ts`
- Create: `tests/Beacon.ClientLab.Playwright/tests/client-lab.spec.ts`

- [ ] **Step 1: Write failing browser tests**

Create Playwright tests proving:

```typescript
test('simulates hello, profile patch, plan, disconnect, quit, emergency restore')
```

Expected UI states:

- client id is `z-fold-7`
- resolution input defaults to `2560x1600`
- refresh defaults to `120`
- changing to `2560x1440` shows a validation error
- plan result includes `virtual-primary`
- disconnect result says lease retained
- quit result says cleanup evaluated
- emergency restore button calls the restore endpoint

- [ ] **Step 2: Verify RED**

Run:

```powershell
pnpm --dir tests/Beacon.ClientLab.Playwright install
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Expected: FAIL because Client Lab does not exist.

- [ ] **Step 3: Implement minimal Client Lab**

Use plain TypeScript and Vite. The UI must be functional and compact:

- Client identity panel.
- Profile editor with width, height, refresh, HDR preference, codec, bitrate cap.
- Capabilities panel.
- Telemetry panel.
- Buttons for hello, patch profile, plan, launch, disconnect, reconnect, quit, emergency restore.
- Result log.

The app must not expose global server settings or display topology policy controls.

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Expected: PASS.

### Task 8: Extraction Map And License Notes

**Files:**
- Create: `docs/extraction-map.md`
- Create: `docs/license-notes.md`

- [ ] **Step 1: Add extraction map**

Create `docs/extraction-map.md` with this table:

```markdown
# Extraction Map

| Source | Planned Use | Copy Source Now | Boundary |
| --- | --- | --- | --- |
| Sunshine | Streaming protocol, capture, encode, audio, input reference | No | Milestone 5 only after focused source audit |
| Apollo | SudoVDA integration, display lifecycle lessons, dynamic app discovery reference | No | Milestone 2/3 only after focused source audit |
| Vibeshine | HDR/driver research reference | No | Research notes only until a specific patch is chosen |
| Vibepollo | Settings complexity anti-patterns and selected research reference | No | Research notes only |
| ApolloDisplayRescue | WPF recovery behavior reference | No | Milestone 4 only after focused source audit |
```
```

- [ ] **Step 2: Add license notes**

Create `docs/license-notes.md`:

```markdown
# License Notes

Beacon Stream is GPL-3.0 because the project may copy or adapt GPL-family source from Sunshine, Apollo, Vibeshine, or Vibepollo.

Milestone 0/1 does not copy upstream source. It creates original contracts, fake backends, tests, and documentation.

Before copying upstream code, add a row to `docs/extraction-map.md` naming:

- source repository
- file path
- license
- copied/adapted/wrapped decision
- Beacon destination path
- reason for reuse
```

- [ ] **Step 3: Validate docs**

Run:

```powershell
$badTerms = @('Copy Source Now \| Yes', 'TO' + 'DO', 'TB' + 'D', 'may' + 'be', 'sh' + 'ould')
foreach ($term in $badTerms) { rg -n $term docs; if ($LASTEXITCODE -eq 0) { exit 1 } }
```

Expected: no matches.

### Task 9: CI And Full Validation

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Add CI**

Create `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
  pull_request:

jobs:
  dotnet:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore Beacon.slnx
      - run: dotnet build Beacon.slnx -warnaserror --no-restore
      - run: dotnet test Beacon.slnx --no-build

  client-lab:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: pnpm/action-setup@v4
        with:
          version: 11
      - uses: actions/setup-node@v4
        with:
          node-version: 24
          cache: pnpm
      - run: pnpm --dir src/Beacon.ClientLab install --frozen-lockfile
      - run: pnpm --dir src/Beacon.ClientLab test
      - run: pnpm --dir tests/Beacon.ClientLab.Playwright install --frozen-lockfile
      - run: pnpm --dir tests/Beacon.ClientLab.Playwright exec playwright install --with-deps chromium
      - run: pnpm --dir tests/Beacon.ClientLab.Playwright test
```

- [ ] **Step 2: Run local static validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir tests/Beacon.ClientLab.Playwright lint
```

Expected: all commands exit 0.

- [ ] **Step 3: Run local dynamic validation**

Run:

```powershell
dotnet test Beacon.slnx
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Expected: all commands exit 0.

- [ ] **Step 4: Sync implementation checkpoint**

Run:

```powershell
git status --short
git add .
git commit -m "Implement Beacon Stream Milestone 0 and 1 foundation"
git push
```

Expected: branch is pushed to GitHub.

### Task 10: Refactor Pass And Final Sync

**Files:**
- Modify only files created by Tasks 1-9.

- [ ] **Step 1: Refactor for implementation holes**

Inspect:

```powershell
$badTerms = @('TO' + 'DO', 'TB' + 'D', 'may' + 'be', 'sh' + 'ould', 'throw new Not' + 'ImplementedException', 'return nu' + 'll')
foreach ($term in $badTerms) { rg -n --glob '!LICENSE' $term .; if ($LASTEXITCODE -eq 0) { exit 1 } }
```

Expected: no matches in source, tests, docs, or Client Lab.

Refactor only these classes when evidence shows duplicated policy:

- `ClientProfilePatcher`
- `SessionPlanner`
- `DisplayLeaseManager`
- fake backend result types

- [ ] **Step 2: Re-run full validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
dotnet test Beacon.slnx
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright lint
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Expected: all commands exit 0.

- [ ] **Step 3: Final sync**

Run:

```powershell
git status --short
git add .
git commit -m "Refactor Beacon Stream Milestone 0 and 1 foundation"
git push
```

Expected: final refactor checkpoint is pushed.

## Plan Self-Review

Spec coverage:

- `REQ-PROJ-*` and `REQ-SYNC-*`: Tasks 1, 9, and 10.
- `REQ-CTRL-*` and `REQ-PROFILE-*`: Tasks 2, 3, 5, 6, and 7.
- `REQ-DISP-*`, `REQ-SESS-*`, and `REQ-MODE-*`: Tasks 3, 4, and 5.
- `REQ-HDR-*`: Task 3.
- `REQ-NET-*`: Task 3 and Task 6.
- `REQ-GAME-*`: Task 8 records source/library boundary; real game scan is Milestone 3 and out of scope for this plan.
- `REQ-REC-*`: Tasks 4, 5, and 7 cover fake recovery commands and API behavior.
- `REQ-TEST-*`: Tasks 2-7 and 9.
- `REQ-M0-*`: Tasks 1 and 8.

Known deferred items by spec:

- Real Windows display backend is Milestone 2.
- Game collection provider implementation is Milestone 3.
- WPF cockpit is Milestone 4.
- Real streaming backend is Milestone 5.
- Android APK is Milestone 6.

Placeholder scan:

- No placeholder markers.
- No soft optionality language.
- No unimplemented-code sentinels.
- No production code before failing tests.
