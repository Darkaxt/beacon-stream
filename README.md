# Beacon Stream

Beacon Stream is a server-authoritative personal game-streaming orchestrator.

Milestone 0/1 covers the control plane, fake backends, planner, profile ownership, phone-free testing, and source-boundary documentation. Milestone 2 adds the real Windows SudoVDA/DisplayConfig lifecycle backend and manual no-phone display probe. Milestone 3 adds the normalized game library model, Steam/Heroic/Hydra/manual providers, SteamGridDB/fallback artwork providers, server/client-lab game selection, and a read-only local game probe. Milestone 4 adds a WPF cockpit for local server administration. Milestone 5 adds the streaming backend boundary, fake no-phone stream lifecycle, and external-process adapter boundary for future Sunshine-compatible integration. Milestone 6 adds a thin Android control-plane APK shell. Milestone 7 adds the server-owned game launch and session ownership cleanup boundary. Real video decode, native input forwarding, and full Windows window enumeration come later.

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

Streaming backend checks:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter FakeStreamingBackendTests
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
```

Milestone 5 uses `FakeStreamingBackend` for deterministic no-phone validation and `ExternalProcessStreamingBackend` as the Windows boundary for future Sunshine-compatible process integration. No Sunshine source is copied by this milestone.

Session ownership checks:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter SessionOwnershipTracker
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
```

Milestone 7 records game launch state and uses server-owned ownership snapshots when deciding whether quit can remove a virtual display. Client-supplied owned-process/window flags are accepted only for backward-compatible request shape and are not the cleanup authority.

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
- `docs/windows-display-backend.md`
