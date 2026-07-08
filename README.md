# Beacon Stream

Beacon Stream is a server-authoritative personal game-streaming orchestrator.

Milestone 0/1 covers the control plane, fake backends, planner, profile ownership, phone-free testing, and source-boundary documentation. Milestone 2 adds the real Windows SudoVDA/DisplayConfig lifecycle backend and manual no-phone display probe. Milestone 3 adds the normalized game library model, Steam/Heroic/Hydra/manual providers, SteamGridDB/fallback artwork providers, server/client-lab game selection, and a read-only local game probe. Milestone 4 adds a WPF cockpit for local server administration. Milestone 5 adds the streaming backend boundary, fake no-phone stream lifecycle, and external-process adapter boundary for future Sunshine-compatible integration. Milestone 6 adds a thin Android control-plane APK shell. Milestone 7 adds the server-owned game launch and session ownership cleanup boundary. Milestone 8 adds the Windows process/window activity inspector. Milestone 9 adds explicit fake-vs-Windows server host composition. Milestone 10 adds explicit streaming backend selection and preflight before display/app side effects. Milestone 11 adds manual recovery actions for stranded windows/processes. Milestone 12 adds persistent client profiles and an explicit pairing boundary for new clients. Milestone 13 adds cockpit profile editing. Milestone 14 adds selected-client recovery/admin actions. Milestone 15 adds telemetry-driven initial planning. Milestone 16 brings Android preflight payloads up to the server planning contract. Milestone 17 adds typed stream connection descriptors. Milestone 18 adds an external-wrapper readiness manifest for capability preflight and descriptor discovery. Milestone 26 makes Android delegate `stream.connection.launchUri` through Android's normal URI launcher after successful launch. Milestone 27 makes disconnect/quit report stream backend stop failures instead of continuing into misleading recovery state. Milestone 28 adds an explicit reset-topology recovery action. Milestone 29 posts Android stream URI handoff onto the Activity UI thread before Android intent launch. Milestone 30 makes the CLI fake endpoint exercise launch and reconnect in the no-phone script. Milestone 31 includes display mode and HDR/SDR rationale in the plan display reason and Client Lab output. Milestone 32 lets Android fetch and show the server-owned game catalog. Milestone 34 lets Android use a loaded catalog entry as the plan/launch target while keeping raw game id fallback. Milestone 35 gives APK-local interaction settings a SharedPreferences-backed persistence boundary without adding server display policy. Milestone 36 exposes read-only display driver/topology health in the admin snapshot and Cockpit diagnostics. Milestone 37 exposes read-only streaming backend health, wrapper manifest capability, and active-stream counts. Milestone 38 reconciles external wrapper process liveness on demand so exited wrappers stop appearing as running streams. Milestone 39 captures bounded stdout/stderr tails from external wrappers for failure diagnostics. Milestone 40 documents and machine-checks the external wrapper manifest contract. Milestone 41 adds a typed client input forwarding contract and no-phone simulator coverage. Milestone 42 publishes input forwarding diagnostics through the operational journal. Milestone 43 adds a source-audited Windows host input sink for display-targeted pointer `move`, `down`, `up`, and `tap` events. Milestone 44 exposes read-only input backend health in the admin snapshot and Cockpit diagnostics. Milestone 45 adds a JVM-tested Android touch-to-pointer mapper and simple single-pointer touch surface. Milestone 46 makes the no-phone simulators send a deterministic down/move/up pointer gesture instead of only a center tap. Milestone 47 adds Windows host keyboard `down`, `up`, and `press` input support with health reporting. Milestone 48 makes Client Lab and the CLI fake endpoint exercise deterministic keyboard input without a phone. Milestone 49 adds Android-side Escape keyboard input forwarding through the same input route. Milestone 50 wires APK-local theme, wake-lock, and decoder debug overlay controls without adding server display policy. Milestone 51 adds Android multi-pointer touch batches through the existing pointer input route. Milestone 52 adds external-wrapper runtime session descriptors so a started wrapper can publish actual stream launch URI/endpoints without phone testing. Milestone 53 adds a no-phone streaming wrapper probe executable that writes the same runtime descriptor contract and stays alive until Beacon stops it. Real video decode, full GameStream input protocol support, controller, and native touch/gesture protocol support come later.

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

`/admin/snapshot` also reports read-only display health under `display`: driver readiness, diagnostic text, topology availability, mirror-mode detection, physical-primary verification, and the current display paths. Cockpit renders the same summary in the local dashboard and diagnostics list.

`/admin/snapshot` reports read-only input health under `inputHealth`: active input backend, readiness, diagnostic text, supported event types, and supported pointer actions. This makes fake no-op input versus Windows `SendInput` visible before phone testing.

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

It can also simulate named telemetry profiles through the no-phone script: hello, profile fetch, allowed profile patch, capabilities, telemetry, plan, launch, pointer gesture input, keyboard input, disconnect, reconnect, plan refresh, quit, and emergency restore.

```powershell
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --telemetry-profile excellent-lan
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --telemetry-profile high-rtt
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --telemetry-profile thermal-battery
```

Supported profiles are `excellent-lan`, `congested-lan`, `high-rtt`, `packet-loss`, `low-bitrate-cap`, and `thermal-battery`. These drive the initial server-computed codec, FPS, bitrate, transport, congestion policy, and reason. This is not a live adaptive bitrate loop.

Plan display reasons include both the selected display mode policy, such as `virtual-primary` or `physical-blackout`, and the HDR/SDR decision. Client Lab renders that display reason with the stream planning reason before launch.

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

`/admin/snapshot` reports read-only streaming backend health under `streamingHealth`: selected backend, executable readiness, optional wrapper manifest readiness, advertised codecs/transports/encoders/capture, HDR10 support, max FPS/bitrate, active stream count, and diagnostics. Cockpit renders the same summary in the dashboard and diagnostics list.

For external-process streaming, Beacon reconciles owned wrapper process liveness whenever health or session state is read. If a wrapper process has exited, the session is marked `exited`, the process is removed from active counts, and the diagnostic journal records the exit reason. This is on-demand state reconciliation, not a polling watchdog.

Beacon captures a bounded tail of wrapper stdout/stderr and includes it in exited-session diagnostics and streaming health diagnostics. The output is treated as opaque wrapper evidence; Beacon does not parse wrapper-specific log formats.

Running stream state includes a `connection` descriptor. Fake mode returns a deterministic `beacon-fake://...` launch URI for no-phone validation. External-process mode can expose a configured connection protocol, launch URI, and endpoint map:

```powershell
$env:BEACON_EXTERNAL_STREAMING_CONNECTION_PROTOCOL='gamestream'
$env:BEACON_EXTERNAL_STREAMING_CONNECTION_LAUNCH_URI='moonlight://beacon/session'
dotnet run --project src\Beacon.Server
```

Endpoint maps are configured under `Beacon:Streaming:ExternalProcess:Connection:Endpoints:*`, for example `rtsp = rtsp://127.0.0.1:48010/beacon`.

External-process mode may also read a wrapper manifest from `Beacon:Streaming:ExternalProcess:ManifestPath` or `BEACON_EXTERNAL_STREAMING_MANIFEST`. When present, Beacon validates codec, FPS, bitrate, transport, and HDR support against the manifest during streaming preflight, before display or launch side effects. Manifest connection fields can provide the stream descriptor unless explicit connection settings override them. The manifest path is also passed to the wrapper as `BEACON_WRAPPER_MANIFEST_PATH`. The documented contract and checked example live in `docs/external-streaming-wrapper-manifest.md` and `docs/examples/external-streaming-manifest.example.json`.

When external-process streaming starts, Beacon prepares a fresh per-session runtime descriptor path, deleting any stale descriptor for the same session id before launching the wrapper. The path is passed as `BEACON_STREAM_SESSION_DESCRIPTOR_PATH` and `--stream-session-descriptor`. If the wrapper writes a runtime descriptor there, Beacon uses it as the running session's connection descriptor and refreshes it on demand when session state or health is read. Runtime descriptors are evidence from the started wrapper, so they take precedence over static manifest connection fields. The checked example lives in `docs/examples/external-streaming-runtime-session.example.json`.

`Beacon.StreamingProbe` is a no-phone wrapper executable for exercising that runtime descriptor boundary before a real Sunshine-compatible wrapper exists. When launched by Beacon it writes the descriptor JSON and stays alive until Beacon stops the process. For standalone descriptor validation, pass `--once`:

```powershell
dotnet run --project src\Beacon.StreamingProbe -- --session z-fold-7-steam-shortcut:3767414131 --display client-z-fold-7 --stream-session-descriptor "$env:TEMP\beacon-runtime-session.json" --once
```

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
curl.exe -X POST http://localhost:5000/admin/recovery/reset-topology
curl.exe -X POST http://localhost:5000/admin/recovery/move-windows-back -H "Content-Type: application/json" -d "{\"minimize\":true}"
curl.exe -X POST http://localhost:5000/admin/recovery/close-virtual-windows
curl.exe -X POST http://localhost:5000/admin/recovery/terminate-virtual-processes
curl.exe -X POST http://localhost:5000/admin/clients/z-fold-7/display/remove
curl.exe -X POST http://localhost:5000/admin/clients/z-fold-7/stream/stop
```

In the WPF cockpit, the Recovery tab exposes the same actions. Reset topology restores the physical primary display first, then moves virtual-display windows back minimized. Display lease removal restores the physical primary display and removes the selected client's virtual display. These are explicit manual escape hatches; normal session cleanup still belongs to the server lifecycle rules.

Owning-client emergency restore is profile-gated by `allowEmergencyRestoreFromClient`. Admin recovery endpoints remain broader local-admin escape hatches, including display lease recovery/removal. Admin physical restore returns `503` with the verified backend error when the laptop panel cannot be confirmed as primary.
Stream stop returns `404` when the selected client has no session plan, and `503` when the streaming backend cannot stop an existing planned session. Client disconnect and quit use the same stop-failure contract; quit does not continue into ownership or display lease cleanup when the streaming backend refuses to stop.

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

`Beacon.Android` is a thin Java APK shell for the client control plane. It can identify the device, patch only APK-allowed client profile fields, fetch and show the server-owned game catalog, report expanded capability and telemetry facts, request/launch a server plan, delegate the server-provided `stream.connection.launchUri` through Android `ACTION_VIEW` on the Activity UI thread, stop/disconnect/quit, forward pointer input including batched multi-pointer touch events, send a simple Escape keyboard press, manage APK-local theme/wake-lock/debug-overlay controls, and call owning-client emergency restore. Plan and launch actions send the profile patch, capabilities, and telemetry first so the server can compute the stream plan from the current client facts. Display behavior policy still belongs to Beacon Server, not the APK. It does not implement real video decode, Moonlight/Sunshine protocol handling, controller, or native touch/gesture protocol support yet.

Client input uses `POST /clients/{clientId}/input`. The server resolves the active session plan and running stream before forwarding a typed input batch to `IClientInputSink`, so the client never supplies display topology or session ownership. The default fake-host sink is a no-op for phone-free testing. In Windows host mode, `WindowsClientInputSink` resolves the active display topology, targets the leased display id, and sends pointer `move`, `down`, `up`, and `tap` commands plus keyboard `down`, `up`, and `press` commands through a fakeable Win32 `SendInput` boundary. Keyboard support is a conservative virtual-key subset for common gaming/navigation keys such as letters, digits, arrows, Escape, Space, Enter, modifiers, and F1-F12; text composition, IME, controller, and GameStream-native input are still future work. The Android shell has a simple touch surface that maps Android pointer down/up plus batched multi-pointer move/cancel events into normalized pointer batches; this is still not a native touch or gesture protocol. Client Lab sends separate deterministic pointer gesture and Escape keyboard press batches, while the CLI fake endpoint includes both in its no-phone scripted input validation. Accepted, rejected, and failed input batches publish `input` diagnostics into `/admin/snapshot`; `inputHealth` reports whether the active sink is `no-op`, `windows-sendinput`, or unknown, including supported pointer and keyboard actions.

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
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-14-recovery-contract.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-15-telemetry-planning.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-16-android-preflight-parity.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-17-stream-connection-descriptor.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-18-wrapper-readiness-manifest.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-27-disconnect-quit-stop-failures.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-28-reset-topology-action.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-29-android-ui-thread-stream-launch.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-30-fake-endpoint-launch-reconnect.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-31-display-mode-reason.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-32-android-game-catalog.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-33-readme-link-integrity.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-34-android-catalog-selection.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-35-android-local-settings-store.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-36-display-health-snapshot.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-37-streaming-health-snapshot.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-38-stream-process-liveness.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-39-wrapper-output-diagnostics.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-40-wrapper-manifest-contract.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-41-input-forwarding-contract.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-42-input-diagnostics.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-43-windows-input-sink.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-44-input-health-snapshot.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-45-android-touch-input.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-46-no-phone-pointer-gesture.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-47-keyboard-input.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-48-no-phone-keyboard-input.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-49-android-keyboard-input.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-50-android-local-settings-controls.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-51-android-multitouch-pointer-batches.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-52-runtime-stream-descriptor.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-53-streaming-wrapper-probe.md`
- `docs/external-streaming-wrapper-manifest.md`
- `docs/source-audits/2026-07-08-windows-input-sink-upstream-audit.md`
- `docs/windows-display-backend.md`
