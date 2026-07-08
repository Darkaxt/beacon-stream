# Windows Display Backend

Beacon's Windows display backend is the real SudoVDA and DisplayConfig integration for Milestone 2. Fast tests use fakes; these commands are the manually runnable boundary for changing Windows display topology without a phone.

## Prerequisites

- SudoVDA is installed and enabled.
- `dotnet build Beacon.slnx -warnaserror` succeeds.
- Visual Studio and WDK are only needed for driver rebuild work, not for running the Beacon display probe.

## Probe Commands

Read-only status:

```powershell
dotnet run --project src/Beacon.DisplayProbe -- status
```

Topology-changing commands:

```powershell
dotnet run --project src/Beacon.DisplayProbe -- ensure --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src/Beacon.DisplayProbe -- primary --client z-fold-7
dotnet run --project src/Beacon.DisplayProbe -- restore-physical
dotnet run --project src/Beacon.DisplayProbe -- remove --client z-fold-7
```

Use `restore-physical` before `remove` when recovering from an active virtual-primary topology. If the legacy display enumeration cannot find the physical panel while the virtual display is primary, Beacon falls back to DisplayConfig path data and restores the non-origin physical candidate rather than failing immediately.

## Server Host Mode

Beacon Server defaults to fake host mode, which is safe for deterministic local tests and does not call the Windows display driver:

```powershell
dotnet run --project src\Beacon.Server
```

Real Windows host composition must be selected explicitly:

```powershell
$env:BEACON_HOST_MODE='windows'
dotnet run --project src\Beacon.Server
```

In Windows host mode, the server registers `WindowsDisplayBackend`, `WindowsGameLauncher`, and `WindowsSessionActivityInspector`. Launch endpoints can create virtual displays and start applications. The streaming backend remains fake until a real wrapper is selected explicitly. `/admin/snapshot` exposes `host.mode` and the selected backend names so the active composition is visible before testing.

## Manual Recovery Actions

Windows host mode also registers `WindowsRecoveryBackend` for explicit local-admin recovery. These endpoints are manual actions:

```powershell
curl.exe -X POST http://localhost:5000/admin/recovery/restore-physical
curl.exe -X POST http://localhost:5000/admin/recovery/move-windows-back -H "Content-Type: application/json" -d "{\"minimize\":true}"
curl.exe -X POST http://localhost:5000/admin/recovery/close-virtual-windows
curl.exe -X POST http://localhost:5000/admin/recovery/terminate-virtual-processes
```

`move-windows-back` targets visible windows intersecting virtual displays, moves them to the physical display, and can minimize them. `close-virtual-windows` sends close requests to those windows. `terminate-virtual-processes` terminates distinct process ids owning those windows, excluding the Beacon process itself.

## Expected Z Fold 7 Check

The manual no-phone validation path is:

```powershell
$ensureExit = 0
try {
    dotnet run --project src/Beacon.DisplayProbe -- ensure --client codex-probe --width 2560 --height 1600 --refresh 120 --hdr prefer
    $ensureExit = $LASTEXITCODE
    dotnet run --project src/Beacon.DisplayProbe -- status
}
finally {
    dotnet run --project src/Beacon.DisplayProbe -- restore-physical
    dotnet run --project src/Beacon.DisplayProbe -- remove --client codex-probe
    dotnet run --project src/Beacon.DisplayProbe -- status
}
exit $ensureExit
```

During the active state, the virtual display should be primary at `2560x1600@120`, the physical display should remain extended at `2560x1600`, and mirror mode should be false. After cleanup, a physical display must be primary again.

## HDR Reporting

HDR is truthful and best-effort. `--hdr prefer` continues in SDR when Windows reports HDR inactive or unavailable and prints the first known reason. `--hdr require` fails before launch when the display backend cannot report HDR enabled.

The current real query uses `DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2` and falls back to the legacy `DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO` query when Info 2 is unavailable.
