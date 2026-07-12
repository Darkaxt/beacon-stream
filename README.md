# Beacon Stream

Beacon Stream is a personal Windows-to-Android game streaming system built around one
Beacon-owned control plane and one Beacon-owned media path. The server owns client
profiles, session planning, virtual-display lifecycle, application launch, ownership,
recovery, and stream policy. The Android app owns only client-local interaction settings,
device facts, game selection, and control requests.

## Current State

Architecture Recovery Gates 0-3 and the Gate 4 evidence model define the current repository
state:

- Core, Server, Cockpit, Client Lab, FakeEndpoint, and Android expose protocol-neutral
  Beacon session state.
- Per-client display leases, the inactive **AND** no-owned-work cleanup rule, physical
  restore, process/window ownership, game discovery, artwork, input, and recovery remain.
- Fake host mode provides deterministic end-to-end control-plane testing.
- A Beacon-owned C++ StreamWorker runs behind typed named-pipe IPC and carries authenticated
  control, input, feedback, and media datagrams over MsQuic.
- The APK has one JNI StreamCore route. Gate 3 proves it against the real Server and Worker on
  the Android emulator with a deterministic, non-decodable access-unit marker.
- Versioned network/hardware fingerprints, raw benchmark samples, server-side scoring,
  automatic reuse decisions, manual always-new runs, history, and persisted plan evidence are
  implemented. Planning rejects missing or stale evidence instead of reverting to telemetry
  heuristics.
- Gate 3 does not claim real video. WGC capture, D3D11 conversion, NVENC H.264, and MediaCodec
  presentation remain Gate 5 work behind the existing Worker/StreamCore contract.

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

The streaming boundary has one production route and one test implementation:

- fake streaming mode uses `FakeStreamingBackend` only for deterministic control-plane tests;
- production and Gate 3 acceptance use `StreamWorkerStreamingBackend`, typed Worker IPC,
  MsQuic, and Android StreamCore. There is no compatibility transport or external wrapper.

## Prerequisites

- Windows 11 for the real host boundaries
- .NET SDK selected by `global.json`
- Node.js and pnpm
- JDK 21, Android SDK 35, and Gradle 8.14.1
- Android platform tools for emulator validation
- Visual Studio C++ tools with CMake 3.25 or newer for StreamWorker builds
- WSL2 Ubuntu for the reproducible local Linux/Android native cross-build
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

Windows mode exercises the real display, launcher, activity, input, recovery, and
StreamWorker boundaries. Real encoded video is intentionally unavailable until Gate 5.

Optional profile and benchmark-evidence persistence:

```powershell
$env:BEACON_CLIENT_PROFILES_PATH="$env:LOCALAPPDATA\BeaconStream\client-profiles.json"
$env:BEACON_BENCHMARK_EVIDENCE_PATH="$env:LOCALAPPDATA\BeaconStream\benchmark-evidence.json"
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
dotnet run --project src\Beacon.FakeEndpoint -- --server http://127.0.0.1:5000 --client-id handheld-1 --name "Handheld 1"
```

Telemetry profiles are `excellent-lan`, `congested-lan`, `high-rtt`, `packet-loss`,
`low-bitrate-cap`, and `thermal-battery`. They exercise operational telemetry only; they do
not select stream settings. Fake host mode seeds deterministic Z Fold 7 benchmark evidence.
Other clients prepare and complete benchmark runs through the Beacon control plane before
planning. Gate 4 transport work will replace submitted fixture samples with measurements from
the production Worker/StreamCore path.

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
input controls, stop/disconnect/quit, and emergency restore. Gate 3 instrumentation drives
the production JNI route; the normal APK still has no claim of moving video before Gate 5.

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

Run the pinned native Windows and Android builds:

```powershell
.\scripts\build-native-windows.ps1
.\scripts\build-native-android-wsl.ps1
```

With `emulator-5554` online, prove the encrypted reliable-stream and datagram exchange
between the pinned Windows and Android MsQuic builds:

```powershell
.\scripts\test-msquic-emulator-interop.ps1
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

With `emulator-5554` online, run the complete Gate 3 matrix through one command:

```powershell
.\scripts\test-gate3.ps1 -Serial emulator-5554
```

The runner covers managed, native Windows, Android native/JNI, Client Lab, live FakeEndpoint,
DisplayProbe, GameProbe, the real Server-to-emulator marker path, tracked-file architecture
absence, and five-channel secret absence. It targets only the requested Android serial.

The architecture guard enumerates Git-tracked source, test, build, script, CI, solution, web,
and properties files. It rejects reintroduction of removed compatibility paths and contracts
instead of maintaining an exception ledger.

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
