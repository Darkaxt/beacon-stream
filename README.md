# Beacon Stream

Beacon Stream is a personal Windows-to-Android game streaming system built around one
Beacon-owned control plane and one Beacon-owned media path. The server owns client
profiles, session planning, virtual-display lifecycle, application launch, ownership,
recovery, and stream policy. The Android app owns only client-local interaction settings,
device facts, game selection, and control requests.

## Current State

Architecture Recovery Gates 0-4 and the R1 integrated production transaction define the current
repository state. Delivery now follows the outcome-driven
[`core-hardening` execution plan](docs/superpowers/plans/2026-08-04-beacon-core-hardening-outcome-gates.md):

- Core, Server, Cockpit, Client Lab, FakeEndpoint, and Android expose protocol-neutral
  Beacon session state.
- Per-client display leases, the inactive **AND** no-owned-work cleanup rule, physical
  restore, process/window ownership, game discovery, artwork, input, and recovery remain.
- Fake host mode provides deterministic end-to-end control-plane testing.
- A Beacon-owned C++ StreamWorker runs behind typed named-pipe IPC and carries authenticated
  control, input, feedback, and media datagrams over MsQuic.
- The APK has one JNI StreamCore route. The normal APK on `emulator-5554` has rendered moving video
  from the production WGC, D3D11, NVENC H.264, MsQuic, and MediaCodec path.
- Versioned network/hardware fingerprints, raw benchmark samples, server-side scoring,
  automatic reuse decisions, manual always-new runs, history, and persisted plan evidence are
  implemented. Planning rejects missing or stale evidence instead of reverting to telemetry
  heuristics.
- R1 is complete. One retained transaction prepared the client-owned display, launched the catalog
  probe, rendered moving H.264 SDR video, delivered authenticated F12 input, survived an active
  disconnect and fresh-ticket reconnect, quit, released ownership, and verified physical-only
  restoration. R1 is integration evidence, not a playable user release.

The playable R2 transaction is paused while the core display and HDR path is hardened. Beacon now
implements one production HDR10 route: Windows Advanced Color capture in FP16, P010 conversion,
NVENC HEVC Main10 encoding, exact HDR metadata transport, and StreamCore/MediaCodec HDR10 decoding
and presentation control. The Worker path is hardware-validated and the Android contract is covered
by unit, native, and emulator tests. A physical HDR Android device is still required to certify that
the standard APK presents the decoded stream as HDR on real hardware; emulator evidence cannot make
that claim. H.264 SDR remains the proven fallback. Audio, tablet input, motion, remaining codecs,
multi-client work, packaging, and UI work remain outside this core checkpoint.

`2560x1600` is not a universal client default. Production planning uses each client's reported
display geometry and supported modes, preferring an exact match and then the closest same-aspect
mode. A Full HD 16:9 client should therefore normally receive `1920x1080`, while a 16:10 client
retains 16:10.

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

Start the production Windows server:

```powershell
dotnet run --project src\Beacon.Server -- --urls https://127.0.0.1:5001
```

The shipped server has one composition: Windows display, launcher, activity,
input, recovery, installed-game discovery, and Beacon StreamWorker. There is no
host or streaming mode selector and no fake backend in production projects.

Start the deterministic test-only server used by process-level simulators:

```powershell
dotnet run --project tests\Beacon.Server.TestHost -- --urls http://127.0.0.1:5000 --Beacon:Security:TestHost=true
```

The test host and all deterministic doubles are compiled only from `tests/`.
Production video availability is reported by the identity-bound Worker capability
handshake; unsupported capture or encoder hardware fails explicitly while network
benchmarking remains available.

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
input controls, stop/disconnect/quit, and emergency restore. Individual production video boundaries
are implemented; R1 requires the normal APK to complete the full production acceptance transaction
before Beacon claims an integrated stream.

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
- Authoritative execution policy: `docs/superpowers/specs/2026-08-04-beacon-core-hardening-execution-design.md`
- Active release-outcome plan: `docs/superpowers/plans/2026-08-04-beacon-core-hardening-outcome-gates.md`
- Recovery inventory: `docs/source-audits/2026-07-10-beacon-architecture-recovery-inventory.md`
- Gates 0-2 plan: `docs/superpowers/plans/2026-07-10-beacon-stream-architecture-recovery-gates-0-2.md`
- Native streaming source audit: `docs/source-audits/2026-07-10-beacon-streamworker-streamcore.md`
- Historical Gates 3-5 record: `docs/superpowers/plans/2026-07-10-beacon-stream-gates-3-5.md`
- Source provenance: `docs/extraction-map.md`
- Windows display boundary: `docs/windows-display-backend.md`
