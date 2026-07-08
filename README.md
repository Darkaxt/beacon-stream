# Beacon Stream

Beacon Stream is a server-authoritative personal game-streaming orchestrator.

Milestone 0/1 covers the control plane, fake backends, planner, profile ownership, phone-free testing, and source-boundary documentation. Milestone 2 adds the real Windows SudoVDA/DisplayConfig lifecycle backend and manual no-phone display probe. Milestone 3 adds the normalized game library model, Steam/Heroic/Hydra/manual providers, SteamGridDB/fallback artwork providers, server/client-lab game selection, and a read-only local game probe. Milestone 4 adds a WPF cockpit for local server administration. Milestone 5 adds the streaming backend boundary, fake no-phone stream lifecycle, and external-process adapter boundary for future Sunshine-compatible integration. Milestone 6 adds a thin Android control-plane APK shell. Milestone 7 adds the server-owned game launch and session ownership cleanup boundary. Milestone 8 adds the Windows process/window activity inspector. Milestone 9 adds explicit fake-vs-Windows server host composition. Milestone 10 adds explicit streaming backend selection and preflight before display/app side effects. Milestone 11 adds manual recovery actions for stranded windows/processes. Milestone 12 adds persistent client profiles and an explicit pairing boundary for new clients. Milestone 13 adds cockpit profile editing. Milestone 14 adds selected-client recovery/admin actions. Milestone 15 adds telemetry-driven initial planning. Milestone 16 brings Android preflight payloads up to the server planning contract. Milestone 17 adds typed stream connection descriptors. Milestone 18 adds an external-wrapper readiness manifest for capability preflight and descriptor discovery. Milestone 26 makes Android delegate `stream.connection.launchUri` through Android's normal URI launcher after successful launch. Real video decode and native input forwarding come later.

## Server Host Mode

The server defaults to deterministic fake host mode:

```powershell
dotnet run --project src\Beacon.Server
```

Windows host mode is explicit because it uses the real SudoVDA/DisplayConfig backend and the real Windows launcher/activity inspector boundaries:

```powershell
$env:BEACON_HOST_MODE='windows'
dotnet run --project src\Beacon.Server
```

Windows mode can create virtual displays and launch applications. The streaming backend is still fake until a real streaming wrapper is selected explicitly. `/admin/snapshot` reports the selected host mode and backend names under `host`.

## Client Profiles And Pairing

The Z Fold 7 profile is seeded as the first known client and keeps the `2560x1600@120` default. Known clients may call `/clients/hello` without pairing. Unknown clients must provide a valid pairing token before the server creates a profile:

```powershell
$env:BEACON_PAIRING_TOKEN='pair-me'
dotnet run --project src\Beacon.Server
```

Profile persistence is enabled by setting a file path:

```powershell
$env:BEACON_CLIENT_PROFILES_PATH="$env:LOCALAPPDATA\BeaconStream\client-profiles.json"
dotnet run --project src\Beacon.Server
```

If no profile path is configured, profiles use an in-memory repository for deterministic development and tests. `/admin/snapshot` reports `profiles.store`, `profiles.location`, and `profiles.pairingEnabled`; it never returns the pairing token.

Profile editing has two different boundaries:

- `/clients/{clientId}/profile` is the APK/client route. It accepts only basic client-owned preferences such as geometry, refresh rate, HDR preference, codec, quality, bitrate cap, audio mode, and keep-app-running behavior.
- `/admin/clients/{clientId}/profile` is the local-admin route used by the WPF cockpit. It can also edit display behavior policy such as display mode, physical-display restore, mirror prohibition, and emergency restore permission.

The fake endpoint can simulate a paired non-phone client:

```powershell
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --client-id handheld-1 --name "Handheld 1" --pairing-token pair-me
```

It can also simulate named telemetry profiles before plan and launch requests:

```powershell
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --telemetry-profile excellent-lan
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --telemetry-profile high-rtt
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --telemetry-profile thermal-battery
```

Supported profiles are `excellent-lan`, `congested-lan`, `high-rtt`, `packet-loss`, `low-bitrate-cap`, and `thermal-battery`. These drive the initial server-computed codec, FPS, bitrate, transport, congestion policy, and reason. This is not a live adaptive bitrate loop.

## Streaming Backend Mode

The streaming backend defaults to fake mode:

```powershell
dotnet run --project src\Beacon.Server
```

External-process streaming is selected separately from host mode:

```powershell
$env:BEACON_STREAMING_BACKEND='external-process'
$env:BEACON_EXTERNAL_STREAMING_EXECUTABLE='C:\Tools\beacon-stream-wrapper.exe'
dotnet run --project src\Beacon.Server
```

The external-process backend preflights the executable path before display creation or game launch. It passes the session plan through command arguments and `BEACON_*` environment variables, records stream state, and stops only its owned wrapper process. This is a wrapper boundary; no Sunshine source is copied.

Running stream state includes a `connection` descriptor. Fake mode returns a deterministic `beacon-fake://...` launch URI for no-phone validation. External-process mode can expose a configured connection protocol, launch URI, and endpoint map:

```powershell
$env:BEACON_EXTERNAL_STREAMING_CONNECTION_PROTOCOL='gamestream'
$env:BEACON_EXTERNAL_STREAMING_CONNECTION_LAUNCH_URI='moonlight://beacon/session'
dotnet run --project src\Beacon.Server
```

Endpoint maps are configured under `Beacon:Streaming:ExternalProcess:Connection:Endpoints:*`, for example `rtsp = rtsp://127.0.0.1:48010/beacon`.

External-process mode may also read a wrapper manifest from `Beacon:Streaming:ExternalProcess:ManifestPath` or `BEACON_EXTERNAL_STREAMING_MANIFEST`. When present, Beacon validates codec, FPS, bitrate, transport, and HDR support against the manifest during streaming preflight, before display or launch side effects. Manifest connection fields can provide the stream descriptor unless explicit connection settings override them. The manifest path is also passed to the wrapper as `BEACON_WRAPPER_MANIFEST_PATH`.

## Local Probes

Display lifecycle checks:

```powershell
dotnet run --project src/Beacon.DisplayProbe -- status
dotnet run --project src/Beacon.DisplayProbe -- ensure --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src/Beacon.DisplayProbe -- restore-physical
```

Display preflight attempts one safe repair before failing: if the first virtual-display ensure fails, Beacon restores the physical primary display and retries the same requested virtual display once. If repair fails, launch still stops before app/stream side effects and the diagnostic journal records the reason.

Physical restore is verified: Beacon queries topology after restore and treats unverified physical-primary state as a recovery failure instead of silently continuing.

Game library checks:

```powershell
dotnet run --project src/Beacon.GameProbe -- scan
dotnet run --project src/Beacon.GameProbe -- scan --json
dotnet run --project src/Beacon.GameProbe -- steam-shortcuts "D:\Steam\userdata\0\config\shortcuts.vdf"
```

`Beacon.GameProbe` is read-only against Steam, Heroic, and Hydra data. It supports explicit paths with `--steam-root`, `--heroic-root`, `--hydra-db`, and `--manual-games`. SteamGridDB artwork uses the `SteamGridDbArtworkProvider` when a caller supplies an API key and artwork root; generated fallback covers are deterministic SVGs from the game title and id.

WPF cockpit:

```powershell
dotnet run --project src/Beacon.Cockpit -- --server http://localhost:5000
```

`Beacon.Cockpit` is a thin local admin UI over the server `/admin` endpoints. It can inspect and edit persisted client profiles and trigger recovery actions. It does not call display drivers, parse Steam/Heroic/Hydra data, or duplicate lifecycle policy; those actions are delegated back to Beacon Server.

Recovery actions:

```powershell
curl.exe -X POST http://localhost:5000/admin/recovery/restore-physical
curl.exe -X POST http://localhost:5000/admin/recovery/move-windows-back -H "Content-Type: application/json" -d "{\"minimize\":true}"
curl.exe -X POST http://localhost:5000/admin/recovery/close-virtual-windows
curl.exe -X POST http://localhost:5000/admin/recovery/terminate-virtual-processes
curl.exe -X POST http://localhost:5000/admin/clients/z-fold-7/display/remove
curl.exe -X POST http://localhost:5000/admin/clients/z-fold-7/stream/stop
```

In the WPF cockpit, the Recovery tab exposes the same actions. Display lease removal restores the physical primary display and removes the selected client's virtual display. These are explicit manual escape hatches; normal session cleanup still belongs to the server lifecycle rules.

Owning-client emergency restore is profile-gated by `allowEmergencyRestoreFromClient`. Admin recovery endpoints remain broader local-admin escape hatches, including display lease recovery/removal. Admin physical restore returns `503` with the verified backend error when the laptop panel cannot be confirmed as primary.
Stream stop returns `404` when the selected client has no session plan, and `503` when the streaming backend cannot stop an existing planned session.

`/admin/snapshot` also returns recent operational diagnostics. These events include display lease decisions, physical-primary restore attempts, recovery actions, and streaming preflight/start/stop failures. The WPF cockpit shows them in the Diagnostics tab together with game-provider diagnostics.

Streaming backend checks:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter FakeStreamingBackendTests
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackend
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter BeaconServiceRegistrationTests
```

Milestone 5 introduced `FakeStreamingBackend` and the command-builder boundary for future Sunshine-compatible process integration. Milestone 10 makes `ExternalProcessStreamingBackend` selectable through explicit configuration and adds preflight so a missing wrapper cannot create a display or launch a game first. Milestone 18 adds a Beacon-owned manifest contract so unsupported codec/FPS/bitrate/transport/HDR plans fail before display or launch side effects. No Sunshine source is copied by these milestones.

Session ownership checks:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter SessionOwnershipTracker
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
```

Milestone 7 records game launch state and uses server-owned ownership snapshots when deciding whether quit can remove a virtual display. Client-supplied owned-process/window flags are accepted only for backward-compatible request shape and are not the cleanup authority.

Windows activity inspector checks:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter WindowsSessionActivityInspector
```

Milestone 8 adds `WindowsSessionActivityInspector`, which can inspect launched-process liveness, child-process liveness, and visible top-level windows on the planned display using fake-testable Windows API boundaries. Milestone 9 wires that inspector into the server only when Windows host mode is selected explicitly.

Android client checks:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

`Beacon.Android` is a thin Java APK shell for the client control plane. It can identify the device, patch only APK-allowed client profile fields, report expanded capability and telemetry facts, request/launch a server plan, delegate the server-provided `stream.connection.launchUri` through Android `ACTION_VIEW`, stop/disconnect/quit, and call owning-client emergency restore. Plan and launch actions send the profile patch, capabilities, and telemetry first so the server can compute the stream plan from the current client facts. Display behavior policy still belongs to Beacon Server, not the APK. It does not implement real video decode, Moonlight/Sunshine protocol handling, or native input forwarding yet.

See:

- `docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md`
- `docs/superpowers/plans/2026-07-07-beacon-stream-milestone-0-1.md`
- `docs/superpowers/plans/2026-07-07-beacon-stream-milestone-2-display-lifecycle.md`
- `docs/superpowers/plans/2026-07-07-beacon-stream-milestone-3-game-collection.md`
- `docs/superpowers/plans/2026-07-07-beacon-stream-milestone-4-wpf-cockpit.md`
- `docs/superpowers/plans/2026-07-07-beacon-stream-milestone-5-streaming-backend.md`
- `docs/superpowers/plans/2026-07-07-beacon-stream-milestone-6-android-client.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-7-session-ownership.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-8-windows-activity-inspector.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-9-windows-host-composition.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-10-streaming-selection.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-11-recovery-actions.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-12-persistent-pairing.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-13-cockpit-profile-editing.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-14-selected-client-admin.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-15-telemetry-planning.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-16-android-preflight-parity.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-17-stream-connection-descriptor.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-18-wrapper-readiness-manifest.md`
- `docs/windows-display-backend.md`
