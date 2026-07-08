# Beacon Stream

Beacon Stream is a server-authoritative personal game-streaming orchestrator.

Milestone 0/1 covers the control plane, fake backends, planner, profile ownership, phone-free testing, and source-boundary documentation. Milestone 2 adds the real Windows SudoVDA/DisplayConfig lifecycle backend and manual no-phone display probe. Milestone 3 adds the normalized game library model, Steam/Heroic/Hydra/manual providers, SteamGridDB/fallback artwork providers, server/client-lab game selection, and a read-only local game probe. Milestone 4 adds a WPF cockpit for local server administration. Milestone 5 adds the streaming backend boundary, fake no-phone stream lifecycle, and external-process adapter boundary for future Sunshine-compatible integration. Milestone 6 adds a thin Android control-plane APK shell. Milestone 7 adds the server-owned game launch and session ownership cleanup boundary. Milestone 8 adds the Windows process/window activity inspector. Milestone 9 adds explicit fake-vs-Windows server host composition. Milestone 10 adds explicit streaming backend selection and preflight before display/app side effects. Milestone 11 adds manual recovery actions for stranded windows/processes. Real video decode and native input forwarding come later.

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

## Local Probes

Display lifecycle checks:

```powershell
dotnet run --project src/Beacon.DisplayProbe -- status
dotnet run --project src/Beacon.DisplayProbe -- ensure --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src/Beacon.DisplayProbe -- restore-physical
```

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

`Beacon.Cockpit` is a thin local admin UI over the server `/admin` endpoints. It does not call display drivers, parse Steam/Heroic/Hydra data, or duplicate lifecycle policy; recovery actions are delegated back to Beacon Server.

Recovery actions:

```powershell
curl.exe -X POST http://localhost:5000/admin/recovery/restore-physical
curl.exe -X POST http://localhost:5000/admin/recovery/move-windows-back -H "Content-Type: application/json" -d "{\"minimize\":true}"
curl.exe -X POST http://localhost:5000/admin/recovery/close-virtual-windows
curl.exe -X POST http://localhost:5000/admin/recovery/terminate-virtual-processes
```

In the WPF cockpit, the Recovery tab exposes the same actions. These are explicit manual escape hatches; normal session cleanup still belongs to the server lifecycle rules.

Streaming backend checks:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter FakeStreamingBackendTests
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackend
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
```

Milestone 5 introduced `FakeStreamingBackend` and the command-builder boundary for future Sunshine-compatible process integration. Milestone 10 makes `ExternalProcessStreamingBackend` selectable through explicit configuration and adds preflight so a missing wrapper cannot create a display or launch a game first. No Sunshine source is copied by these milestones.

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

`Beacon.Android` is a thin Java APK shell for the client control plane. It can identify the device, patch only APK-allowed client profile fields, report capabilities and telemetry, request/launch a server plan, stop/disconnect/quit, and call owning-client emergency restore. It does not implement real video decode, Moonlight/Sunshine protocol handling, or native input forwarding yet.

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
- `docs/windows-display-backend.md`
