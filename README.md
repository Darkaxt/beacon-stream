# Beacon Stream

Beacon Stream is a server-authoritative personal game-streaming orchestrator.

Milestone 0/1 covers the control plane, fake backends, planner, profile ownership, phone-free testing, and source-boundary documentation. Milestone 2 adds the real Windows SudoVDA/DisplayConfig lifecycle backend and manual no-phone display probe. Milestone 3 adds the normalized game library model, Steam/Heroic/Hydra/manual providers, SteamGridDB/fallback artwork providers, server/client-lab game selection, and a read-only local game probe. Milestone 4 adds a WPF cockpit for local server administration. Milestone 5 adds the streaming backend boundary, fake no-phone stream lifecycle, and external-process adapter boundary for future Sunshine-compatible integration. Milestone 6 adds a thin Android control-plane APK shell. Milestone 7 adds the server-owned game launch and session ownership cleanup boundary. Milestone 8 adds the Windows process/window activity inspector. Milestone 9 adds explicit fake-vs-Windows server host composition. Milestone 10 adds explicit streaming backend selection and preflight before display/app side effects. Milestone 11 adds manual recovery actions for stranded windows/processes. Milestone 12 adds persistent client profiles and an explicit pairing boundary for new clients. Milestone 13 adds cockpit profile editing. Milestone 14 adds selected-client recovery/admin actions. Milestone 15 adds telemetry-driven initial planning. Milestone 16 brings Android preflight payloads up to the server planning contract. Milestone 17 adds typed stream connection descriptors. Milestone 18 adds an external-wrapper readiness manifest for capability preflight and descriptor discovery. Milestone 26 makes Android delegate `stream.connection.launchUri` through Android's normal URI launcher after successful launch. Milestone 27 makes disconnect/quit report stream backend stop failures instead of continuing into misleading recovery state. Milestone 28 adds an explicit reset-topology recovery action. Milestone 29 posts Android stream URI handoff onto the Activity UI thread before Android intent launch. Milestone 30 makes the CLI fake endpoint exercise launch and reconnect in the no-phone script. Milestone 31 includes display mode and HDR/SDR rationale in the plan display reason and Client Lab output. Milestone 32 lets Android fetch and show the server-owned game catalog. Milestone 34 lets Android use a loaded catalog entry as the plan/launch target while keeping raw game id fallback. Milestone 35 gives APK-local interaction settings a SharedPreferences-backed persistence boundary without adding server display policy. Milestone 36 exposes read-only display driver/topology health in the admin snapshot and Cockpit diagnostics. Milestone 37 exposes read-only streaming backend health, wrapper manifest capability, and active-stream counts. Milestone 38 reconciles external wrapper process liveness on demand so exited wrappers stop appearing as running streams. Milestone 39 captures bounded stdout/stderr tails from external wrappers for failure diagnostics. Milestone 40 documents and machine-checks the external wrapper manifest contract. Milestone 41 adds a typed client input forwarding contract and no-phone simulator coverage. Milestone 42 publishes input forwarding diagnostics through the operational journal. Milestone 43 adds a source-audited Windows host input sink for display-targeted pointer `move`, `down`, `up`, and `tap` events. Milestone 44 exposes read-only input backend health in the admin snapshot and Cockpit diagnostics. Milestone 45 adds a JVM-tested Android touch-to-pointer mapper and simple single-pointer touch surface. Milestone 46 makes the no-phone simulators send a deterministic down/move/up pointer gesture instead of only a fixed center tap. Milestone 47 adds Windows host keyboard `down`, `up`, and `press` input support with health reporting. Milestone 48 makes Client Lab and the CLI fake endpoint exercise deterministic keyboard input without a phone. Milestone 49 adds Android-side Escape keyboard input forwarding through the same input route. Milestone 50 wires APK-local theme, wake-lock, and decoder debug overlay controls without adding server display policy. Milestone 51 adds Android multi-pointer touch batches through the existing pointer input route. Milestone 52 adds external-wrapper runtime session descriptors so a started wrapper can publish actual stream launch URI/endpoints without phone testing. Milestone 53 adds a no-phone streaming wrapper probe executable that writes the same runtime descriptor contract and stays alive until Beacon stops it. Milestone 54 adds a server API smoke test that launches that probe through the real Windows external-process runner while keeping display and game side effects fake. Milestone 55 lets the CLI fake endpoint optionally assert that a launched session exposes a usable `stream.connection` descriptor before it sends input. Milestone 56 adds a stream-handoff-only fake endpoint script mode and an in-memory server integration test that launches the real wrapper probe, validates immediate GameStream-style connection metadata, observes the runtime descriptor, and verifies cleanup. Milestone 57 launches external wrapper processes from the wrapper executable directory so relative config/log assumptions do not depend on Beacon's own working directory. Milestone 62 lets the streaming probe supervise a configured child process before publishing runtime descriptor evidence, which gives Beacon a no-phone harness for wrapper process ownership before real Sunshine-compatible integration. Milestone 63 adds server-owned configuration for that child process and preflights a missing configured child before display or app side effects. Milestone 64 adds a server-level no-phone smoke that launches the probe with a real child process and verifies child exit reconciliation through the server stream API. Milestone 65 exposes structured wrapper-child readiness in admin and Cockpit streaming health. Milestone 66 rejects wrapper child arguments without a child executable before launch. Milestone 76 adds a server-owned external wrapper argument template with preflight validation. Milestone 77 adds an Android in-app native stream boundary for emulator-testable `beacon-test` descriptors. Milestone 78 adds a selectable `beacon-test` streaming backend and an Android in-app color-bars test-pattern surface. Milestone 79 makes Android validate endpoint-only GameStream/Moonlight maps and report incomplete handoff versus complete-but-decoder-not-implemented diagnostics. Milestone 80 replaces Android's hardcoded codec facts with a device-derived decoder capability probe. Milestone 81 enriches Android telemetry reports with device-derived battery, thermal, and Wi-Fi transport facts. Real video decode, full GameStream input protocol support, controller, and native touch/gesture protocol support come later.

Milestone 67 adds explicit inactive-client disconnect cleanup: empty/default disconnects retain the display lease for reconnect, while `clientActive: false` disconnects evaluate the server-owned cleanup gate and remove the lease only when no owned work remains.

Milestone 68 adds explicit client beacon lease preparation: active beacon prepares the per-client display lease before launch, while inactive beacon evaluates the same server-owned cleanup gate without timers or watchdogs.

Milestone 69 adds Client Lab active/inactive beacon controls and Playwright coverage so the browser simulator exercises the same lifecycle action without a phone.

Milestone 70 splits prepared display leases from session activation: active beacon creates/verifies the per-client virtual display without making it primary, while launch remains the point that activates virtual-primary for the session.

Milestone 71 adds the no-phone `DisplayProbe prepare` command for real SudoVDA lease preparation checks.

Milestone 72 lets launch activation reuse a display prepared by an earlier beacon instead of recreating it.

Milestone 73 hardens recovery when physical-primary restore reports stale topology by removing the selected client lease and verifying physical-primary health again.

Milestone 74 routes owning-client APK emergency restore through the same client-scoped recovery path as local admin recovery.

Milestone 75 completes the Android local-settings editing boundary for version 1: touch layout, multitouch, controller overlay marker, haptics, UI density, theme, wake lock, and decoder overlay remain APK-local and do not affect server display policy.

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

It can also simulate named telemetry profiles through the no-phone script: hello, profile fetch, allowed profile patch, capabilities, telemetry, active beacon, plan, launch, optional stream connection assertion, pointer gesture input, keyboard input, disconnect, reconnect, plan refresh, quit, and emergency restore.

```powershell
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --telemetry-profile excellent-lan
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --telemetry-profile high-rtt
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --telemetry-profile thermal-battery
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --require-stream-connection true
dotnet run --project src\Beacon.FakeEndpoint -- --server http://localhost:5000 --require-stream-connection true --end-after-stream-connection true
```

Supported profiles are `excellent-lan`, `congested-lan`, `high-rtt`, `packet-loss`, `low-bitrate-cap`, and `thermal-battery`. These drive the initial server-computed codec, FPS, bitrate, transport, congestion policy, and reason. This is not a live adaptive bitrate loop.

`--end-after-stream-connection true` stops the fake endpoint script immediately after the required stream descriptor is validated. This is useful for no-phone wrapper handoff tests where the stream must remain running long enough for runtime descriptor evidence before an explicit disconnect.

Plan display reasons include both the selected display mode policy, such as `virtual-primary` or `physical-blackout`, and the HDR/SDR decision. Client Lab renders that display reason with the stream planning reason before launch. Client Lab also has Active Beacon and Inactive Beacon controls that send only client activity state; Beacon Server owns the resulting display lease and cleanup policy. Active beacon prepares the client's virtual display as an extended/non-primary lease; launch activates virtual-primary for the actual session.

## Streaming Backend Mode

The streaming backend defaults to fake mode:

```powershell
dotnet run --project src\Beacon.Server
```

For no-phone APK/native-stream validation, Beacon can run a deterministic endpoint-only test-pattern backend:

```powershell
$env:BEACON_STREAMING_BACKEND='beacon-test'
dotnet run --project src\Beacon.Server
```

This backend starts a normal Beacon stream session but advertises `protocol=beacon-test`, no `launchUri`, and `video=beacon-test://pattern/color-bars`. The Android APK consumes that endpoint in-app and renders a color-bars surface. This is a diagnostic stream path for emulator/server handoff testing, not Moonlight/GameStream video decode.

External-process streaming is selected separately from host mode:

```powershell
$env:BEACON_STREAMING_BACKEND='external-process'
$env:BEACON_EXTERNAL_STREAMING_EXECUTABLE='C:\Tools\beacon-stream-wrapper.exe'
dotnet run --project src\Beacon.Server
```

The external-process backend preflights the executable path before display creation or game launch. It launches the wrapper with the executable directory as the process working directory, passes the session plan through command arguments and `BEACON_*` environment variables, records stream state, and stops only its owned wrapper process. This is a wrapper boundary; no Sunshine source is copied.

Wrappers that need a stable command-line shape can use `Beacon:Streaming:ExternalProcess:ArgumentTemplate` or `BEACON_EXTERNAL_STREAMING_ARGUMENT_TEMPLATE`. Tokens are replaced with quoted values before launch. Supported tokens are `{sessionId}`, `{clientId}`, `{appId}`, `{displayId}`, `{codec}`, `{fps}`, `{bitrateMbps}`, `{transport}`, `{sessionDescriptorPath}`, `{manifestPath}`, `{connectionProtocol}`, `{connectionLaunchUri}`, `{wrapperChildExecutablePath}`, and `{wrapperChildArguments}`. Unknown, empty, or unclosed tokens make streaming health not ready and fail preflight before display/app side effects.

`/admin/snapshot` reports read-only streaming backend health under `streamingHealth`: selected backend, executable readiness, optional wrapper child executable readiness, optional wrapper manifest readiness, advertised connection protocol/launch URI/endpoints, codecs/transports/encoders/capture, HDR10 support, max FPS/bitrate, active stream count, and diagnostics. Cockpit renders the same summary in the dashboard and diagnostics list.

For external-process streaming, Beacon reconciles owned wrapper process liveness whenever health or session state is read. If a wrapper process has exited, the session is marked `exited`, the process is removed from active counts, and the diagnostic journal records the exit reason. This is on-demand state reconciliation, not a polling watchdog.

Beacon captures a bounded tail of wrapper stdout/stderr and includes it in exited-session diagnostics and streaming health diagnostics. The output is treated as opaque wrapper evidence; Beacon does not parse wrapper-specific log formats.

Running stream state includes a `connection` descriptor. Fake mode returns a deterministic `beacon-fake://...` launch URI for no-phone validation. External-process mode can expose a configured connection protocol, launch URI, and endpoint map:

```powershell
$env:BEACON_EXTERNAL_STREAMING_CONNECTION_PROTOCOL='gamestream'
$env:BEACON_EXTERNAL_STREAMING_CONNECTION_LAUNCH_URI='moonlight://beacon/session'
dotnet run --project src\Beacon.Server
```

Endpoint maps are configured under `Beacon:Streaming:ExternalProcess:Connection:Endpoints:*`, for example `rtsp = rtsp://127.0.0.1:48010/beacon`.

For Sunshine/GameStream-compatible wrappers with standard port layout, Beacon can derive the endpoint map from a host and Sunshine base port:

```powershell
$env:BEACON_EXTERNAL_STREAMING_SUNSHINE_HOST='127.0.0.1'
$env:BEACON_EXTERNAL_STREAMING_SUNSHINE_BASE_PORT='47989'
dotnet run --project src\Beacon.Server
```

This advertises `gamestream` and derives the documented Sunshine HTTPS, HTTP, web, RTSP, video, control, audio, and mic endpoints. Explicit `Connection:Endpoints:*` entries override the derived endpoint for the same role. Beacon does not invent a Moonlight launch URI from this profile; use explicit connection config, manifest fields, or the runtime descriptor for launch URI handoff.

External-process mode may also read a wrapper manifest from `Beacon:Streaming:ExternalProcess:ManifestPath` or `BEACON_EXTERNAL_STREAMING_MANIFEST`. When present, Beacon validates codec, FPS, bitrate, transport, and HDR support against the manifest during streaming preflight, before display or launch side effects. Manifest connection fields can provide the stream descriptor unless explicit connection settings override them. The manifest path is also passed to the wrapper as `BEACON_WRAPPER_MANIFEST_PATH`. The documented contract and checked example live in `docs/external-streaming-wrapper-manifest.md` and `docs/examples/external-streaming-manifest.example.json`.

When external-process streaming starts, Beacon prepares a fresh per-session runtime descriptor path, deleting any stale descriptor for the same session id before launching the wrapper. The path is passed as `BEACON_STREAM_SESSION_DESCRIPTOR_PATH` and `--stream-session-descriptor`. If the wrapper writes a runtime descriptor there, Beacon uses it as the running session's connection descriptor and refreshes it on demand when session state or health is read. Static manifest or explicit connection settings provide the immediate client handoff; runtime descriptors are evidence from the started wrapper and take precedence once present. The checked example lives in `docs/examples/external-streaming-runtime-session.example.json`.

`Beacon.StreamingProbe` is a no-phone wrapper executable for exercising that runtime descriptor boundary before a real Sunshine-compatible wrapper exists. When launched by Beacon it writes the descriptor JSON and stays alive until Beacon stops the process. It can also supervise a child process through `--child-executable` / `BEACON_WRAPPER_CHILD_EXECUTABLE` and `--child-arguments` / `BEACON_WRAPPER_CHILD_ARGUMENTS`. In child mode, the probe starts the child before writing runtime descriptor evidence, exits with the child exit code if the child exits first, and stops the child when Beacon stops the wrapper. For standalone descriptor validation, pass `--once`:

```powershell
dotnet run --project src\Beacon.StreamingProbe -- --session z-fold-7-steam-shortcut:3767414131 --display client-z-fold-7 --stream-session-descriptor "$env:TEMP\beacon-runtime-session.json" --once
```

Child-process validation example:

```powershell
dotnet run --project src\Beacon.StreamingProbe -- --session z-fold-7-steam-shortcut:3767414131 --display client-z-fold-7 --stream-session-descriptor "$env:TEMP\beacon-runtime-session.json" --child-executable "C:\Tools\sunshine.exe" --child-arguments "--config sunshine.json"
```

Beacon Server can pass the same child settings to any configured wrapper:

```powershell
$env:BEACON_STREAMING_BACKEND='external-process'
$env:BEACON_EXTERNAL_STREAMING_EXECUTABLE='C:\Tools\Beacon.StreamingProbe.exe'
$env:BEACON_EXTERNAL_STREAMING_WRAPPER_CHILD_EXECUTABLE='C:\Tools\sunshine.exe'
$env:BEACON_EXTERNAL_STREAMING_WRAPPER_CHILD_ARGUMENTS='--config sunshine.json'
dotnet run --project src\Beacon.Server
```

Equivalent configuration keys are `Beacon:Streaming:ExternalProcess:Wrapper:ChildExecutablePath` and `Beacon:Streaming:ExternalProcess:Wrapper:ChildArguments`. When a wrapper child executable is configured, Beacon preflights that path before display creation or app launch.

## Local Probes

Display lifecycle checks:

```powershell
dotnet run --project src/Beacon.DisplayProbe -- status
dotnet run --project src/Beacon.DisplayProbe -- prepare --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src/Beacon.DisplayProbe -- ensure --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src/Beacon.DisplayProbe -- restore-physical
```

`prepare` verifies the client virtual display as an extended/non-primary lease. `ensure` activates the virtual display as primary for a session. Display preflight attempts one safe repair before failing: if the first virtual-display prepare or activation fails, Beacon restores the physical primary display and retries the same requested virtual display once. If repair fails, launch still stops before app/stream side effects and the diagnostic journal records the reason.

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

Owning-client emergency restore is profile-gated by `allowEmergencyRestoreFromClient` and uses client-scoped display recovery for that client's lease. If physical-primary restore verification is stale or fails, the owning-client emergency path can remove that client's virtual display lease and verify the physical display again. Admin recovery endpoints remain broader local-admin escape hatches, including display lease recovery/removal. Admin physical restore returns `503` with the verified backend error when the laptop panel cannot be confirmed as primary.
Stream stop returns `404` when the selected client has no session plan, and `503` when the streaming backend cannot stop an existing planned session. Client disconnect and quit use the same stop-failure contract; quit does not continue into ownership or display lease cleanup when the streaming backend refuses to stop. Empty/default disconnect represents a still-active client and retains the leased display. Explicit `clientActive: false` disconnect stops streaming, evaluates server-owned activity, restores physical primary through display cleanup, and removes the lease only when the client is inactive and no owned work remains. `POST /clients/{clientId}/beacon` lets an active client prepare its display lease before launch without taking primary from the laptop; `{ "active": false }` evaluates the same cleanup gate without adding a timeout or watchdog.

`/admin/snapshot` also returns recent operational diagnostics. These events include display lease decisions, physical-primary restore attempts, recovery actions, and streaming preflight/start/stop failures. The WPF cockpit shows them in the Diagnostics tab together with game-provider diagnostics.

Streaming backend checks:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter FakeStreamingBackendTests
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackend
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter WindowsRunnerCreatesStartInfoWithWrapperWorkingDirectory
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter SunshineEndpointProfile
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter GetHealthAsyncReportsSunshineEndpointProfile
dotnet test tests/Beacon.StreamingProbe.Tests/Beacon.StreamingProbe.Tests.csproj
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter FakeEndpointScriptCompletesAgainstStreamingProbeWrapper
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter FakeEndpointScriptCompletesAgainstStreamingProbeWithSunshineProfile
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

Emulator smoke check:

```powershell
adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk
adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity
```

`Beacon.Android` is a thin Java APK shell for the client control plane. It can identify the device, patch only APK-allowed client profile fields, fetch and show the server-owned game catalog, report device-derived decoder capability facts and telemetry facts, request/launch a server plan, delegate the server-provided `stream.connection.launchUri` through Android `ACTION_VIEW` on the Activity UI thread, start an in-app diagnostic native stream for explicitly supported `beacon-test` endpoint-only descriptors, render the `beacon-test://pattern/color-bars` stream as an in-app color-bars surface, stop/disconnect/quit, forward pointer input including batched multi-pointer touch events, send a simple Escape keyboard press, manage APK-local touch layout, multitouch, controller overlay marker, haptics, UI density, theme, wake-lock, and decoder overlay controls, and call owning-client emergency restore. Plan and launch actions send the profile patch, capabilities, and telemetry first so the server can compute the stream plan from the current client facts. Display behavior policy still belongs to Beacon Server, not the APK. It does not implement real video decode, Moonlight/Sunshine protocol handling, controller protocol, or native touch/gesture protocol support yet.

Capability reporting uses Android `MediaCodecList` through a fakeable probe boundary. The APK reports H.264, HEVC, AV1, low-latency decoder evidence, current screen mode, and conservative HDR10 support when both decoder and screen HDR evidence are present. `virtualDisplayHdrSupported` remains false until the server-side Windows display chain proves that boundary.

Telemetry reporting preserves manually entered RTT, packet-loss, decoder-load, and bandwidth samples for emulator testing, then enriches battery percent, thermal state, and Wi-Fi transport from Android system services when available. It does not run active network probes, packet-loss measurement loops, or timeout-based collection in the APK.

The APK also exposes manual active and inactive beacon actions; these only report client activity to Beacon Server and do not move display policy into the APK.

If the server returns a stream connection with endpoints but no `launchUri`, the APK routes only explicitly supported native protocols to its in-app stream client. The current diagnostic client supports `beacon-test://pattern/color-bars` for emulator/debug validation, records a visible native stream status, and shows the test-pattern surface. Endpoint-only GameStream/Moonlight descriptors still record a visible diagnostic instead of pretending native decode exists.

For endpoint-only GameStream/Moonlight descriptors, Android validates the server-owned endpoint map before reporting the native stream diagnostic. Required roles are `rtsp`, `video`, `control`, and `audio`. Missing roles produce an incomplete-handoff diagnostic; complete maps produce a clear "native GameStream decode is not implemented yet" diagnostic. This is the emulator-testable preflight contract for future decoder transport work.

Client input uses `POST /clients/{clientId}/input`. The server resolves the active session plan and running stream before forwarding a typed input batch to `IClientInputSink`, so the client never supplies display topology or session ownership. The default fake-host sink is a no-op for phone-free testing. In Windows host mode, `WindowsClientInputSink` resolves the active display topology, targets the leased display id, and sends pointer `move`, `down`, `up`, and `tap` commands plus keyboard `down`, `up`, and `press` commands through a fakeable Win32 `SendInput` boundary. Keyboard support is a conservative virtual-key subset for common gaming/navigation keys such as letters, digits, arrows, Escape, Space, Enter, modifiers, and F1-F12; text composition, IME, controller, and GameStream-native input are still future work. The Android shell has a simple touch surface that maps Android pointer down/up plus batched multi-pointer move/cancel events into normalized pointer batches; this is still not a native touch or gesture protocol. Client Lab sends active/inactive beacon actions, separate deterministic pointer gesture, and Escape keyboard press batches, while the CLI fake endpoint includes beacon and input in its no-phone scripted validation. Accepted, rejected, and failed input batches publish `input` diagnostics into `/admin/snapshot`; `inputHealth` reports whether the active sink is `no-op`, `windows-sendinput`, or unknown, including supported pointer and keyboard actions.

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
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-54-wrapper-smoke.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-55-fake-endpoint-stream-assertion.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-56-fake-endpoint-wrapper-integration.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-57-wrapper-working-directory.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-58-sunshine-port-profile.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-59-sunshine-profile-wrapper-integration.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-60-streaming-health-endpoints.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-61-android-connection-diagnostics.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-62-streaming-probe-child-process.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-63-wrapper-child-config.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-64-wrapper-child-smoke.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-65-wrapper-child-health.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-66-wrapper-child-config-invariant.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-67-inactive-disconnect-cleanup.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-68-client-beacon-lease.md`
- `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-69-client-lab-beacon.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-70-prepared-display-lease.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-71-display-probe-prepare.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-72-activation-reuses-prepared-display.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-73-recover-after-restore-failure.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-74-apk-emergency-recovery-parity.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-75-android-local-settings-ui-completion.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-76-wrapper-argument-template.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-77-android-native-stream-boundary.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-78-beacon-test-stream-surface.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-79-android-gamestream-endpoint-preflight.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-80-android-device-capability-probe.md`
- `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-81-android-device-telemetry-probe.md`
- `docs/external-streaming-wrapper-manifest.md`
- `docs/source-audits/2026-07-08-windows-input-sink-upstream-audit.md`
- `docs/windows-display-backend.md`
