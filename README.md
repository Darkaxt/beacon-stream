# Beacon Stream

Beacon Stream is a personal Windows-to-Android game streaming system built around one
Beacon-owned control plane and one future Beacon-owned media path. The server owns client
profiles, session planning, virtual-display lifecycle, application launch, ownership,
recovery, and stream policy. The Android app owns only client-local interaction settings,
device facts, game selection, and control requests.

## Current State

Architecture Recovery Gates 0-2 define the current repository state:

- Core, Server, Cockpit, Client Lab, FakeEndpoint, and Android expose protocol-neutral
  Beacon session state.
- Per-client display leases, the inactive **AND** no-owned-work cleanup rule, physical
  restore, process/window ownership, game discovery, artwork, input, and recovery remain.
- Fake host mode provides deterministic end-to-end control-plane testing.
- Windows host mode intentionally fails media preflight with
  `Beacon StreamWorker is not implemented during architecture recovery.`
- The APK intentionally performs no media handoff during recovery. Generic Android
  codec, surface, and decoder primitives remain for the future StreamCore boundary.

The approved Gates 3-5 implementation is a source-audited Beacon StreamWorker/StreamCore
vertical slice: fake transport proof first, benchmark traffic through the production
transport, then real H.264 video to the Android emulator.

## Architecture

| Component | Responsibility |
| --- | --- |
| `Beacon.Core` | Client facts, profiles, plans, display leases, games, ownership, recovery, diagnostics, and protocol-neutral streaming contracts. |
| `Beacon.Server` | HTTP control plane, lifecycle orchestration, persistence, host composition, and admin snapshots. |
| `Beacon.Platform.Windows` | SudoVDA/DisplayConfig, Windows application launch, process/window inspection, input injection, and physical recovery. |
| `Beacon.Cockpit` | Local WPF administration for clients, sessions, games, display state, recovery, and diagnostics. |
| `Beacon.ClientLab` | Browser-based remote-client simulator for control-plane and lifecycle testing. |
| `Beacon.FakeEndpoint` | Deterministic command-line client script for phone-free integration checks. |
| `Beacon.DisplayProbe` | Read-only display status plus explicit manual prepare/activate/restore commands. |
| `Beacon.GameProbe` | Read-only Steam, Heroic, Hydra, and manual-library inspection. |
| `Beacon.Android` | Thin APK for client facts, catalog selection, local settings, input requests, and recovery controls. |

The production media boundary has exactly two current implementations:

- fake host mode uses `FakeStreamingBackend` for deterministic tests;
- Windows host mode uses `UnavailableStreamingBackend` and fails closed until StreamWorker
  is implemented.

## Prerequisites

- Windows 11 for the real host boundaries
- .NET SDK selected by `global.json`
- Node.js and pnpm
- JDK 17 or newer, Android SDK 35, and Gradle 8.14.1
- Android platform tools for emulator validation
- SudoVDA only when exercising the real display backend

## Server

Start deterministic fake host mode on the endpoint expected by the simulators:

```powershell
$env:ASPNETCORE_URLS='http://127.0.0.1:5000'
dotnet run --project src\Beacon.Server
```

Start real Windows host composition:

```powershell
$env:ASPNETCORE_URLS='http://127.0.0.1:5000'
$env:BEACON_HOST_MODE='windows'
dotnet run --project src\Beacon.Server
```

Windows mode can exercise the real display, launcher, activity, input, and recovery
boundaries. Launch media preflight fails before display or application side effects until
StreamWorker lands.

Optional profile persistence and pairing:

```powershell
$env:BEACON_CLIENT_PROFILES_PATH="$env:LOCALAPPDATA\BeaconStream\client-profiles.json"
$env:BEACON_PAIRING_TOKEN='pair-me'
```

## Phone-Free Clients

Run Client Lab:

```powershell
pnpm --dir src\Beacon.ClientLab install --frozen-lockfile
pnpm --dir src\Beacon.ClientLab dev
```

Run the deterministic endpoint script:

```powershell
dotnet run --project src\Beacon.FakeEndpoint -- --server http://127.0.0.1:5000
dotnet run --project src\Beacon.FakeEndpoint -- --server http://127.0.0.1:5000 --telemetry-profile high-rtt
dotnet run --project src\Beacon.FakeEndpoint -- --server http://127.0.0.1:5000 --client-id handheld-1 --name "Handheld 1" --pairing-token pair-me
```

Telemetry profiles are `excellent-lan`, `congested-lan`, `high-rtt`, `packet-loss`,
`low-bitrate-cap`, and `thermal-battery`. They provide deterministic planner inputs; they
are not a live adaptation loop.

## Local Tools

Read display state without changing topology:

```powershell
dotnet run --project src\Beacon.DisplayProbe -- status
```

Explicit display-changing commands are manual operations:

```powershell
dotnet run --project src\Beacon.DisplayProbe -- prepare --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src\Beacon.DisplayProbe -- ensure --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src\Beacon.DisplayProbe -- restore-physical
```

Inspect installed games:

```powershell
dotnet run --project src\Beacon.GameProbe -- scan
dotnet run --project src\Beacon.GameProbe -- scan --json
```

Start the WPF cockpit:

```powershell
dotnet run --project src\Beacon.Cockpit
```

## Android APK

Build and install the recovery APK:

```powershell
gradle -p src\Beacon.Android test assembleDebug
adb install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk
adb shell am start -W -n dev.beacon.android/.BeaconActivity
```

For the standard Android emulator, the APK server URL is `http://10.0.2.2:5000`.
Validate catalog selection, local settings persistence, capability and telemetry reports,
input controls, stop/disconnect/quit, and emergency restore. A successful launch displays
the explicit StreamWorker recovery message and does not open a media surface or another app.

## Validation

Run the .NET matrix:

```powershell
dotnet restore Beacon.slnx
dotnet format Beacon.slnx --verify-no-changes --no-restore
dotnet build Beacon.slnx -warnaserror --no-restore
dotnet test Beacon.slnx --no-build
```

Run Android checks:

```powershell
gradle -p src\Beacon.Android test assembleDebug
```

Run Client Lab and browser checks:

```powershell
pnpm --dir src\Beacon.ClientLab install --frozen-lockfile
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright install --frozen-lockfile
pnpm --dir tests\Beacon.ClientLab.Playwright lint
pnpm --dir tests\Beacon.ClientLab.Playwright test
```

The architecture guard scans every runtime and test project. It rejects reintroduction of
removed compatibility paths and contracts instead of maintaining an exception ledger.

## Lifecycle Invariants

- One virtual display lease belongs to one client.
- Active beacon prepares the lease; launch activates the session display policy.
- Stream disconnect does not independently destroy the display.
- Cleanup requires the client to be inactive **AND** no owned process, child process, or
  new top-level window to remain.
- Physical restore is verified rather than assumed.
- No timer decides display or session ownership.
- Client-local interaction settings never become server-global display policy.

## Authority

- Authoritative requirements: `docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md`
- Recovery inventory: `docs/source-audits/2026-07-10-beacon-architecture-recovery-inventory.md`
- Gates 0-2 plan: `docs/superpowers/plans/2026-07-10-beacon-stream-architecture-recovery-gates-0-2.md`
- Native streaming source audit: `docs/source-audits/2026-07-10-beacon-streamworker-streamcore.md`
- Gates 3-5 plan: `docs/superpowers/plans/2026-07-10-beacon-stream-gates-3-5.md`
- Source provenance: `docs/extraction-map.md`
- Windows display boundary: `docs/windows-display-backend.md`
