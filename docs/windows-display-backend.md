# Windows Display Backend

Beacon's Windows display backend is the real SudoVDA and DisplayConfig integration for Milestone 2. Fast tests use fakes; these commands are the manually runnable boundary for changing Windows display topology without a phone.

## Prerequisites

- SudoVDA is installed and enabled.
- Beacon owns its SudoVDA control session and heartbeat while Beacon display leases exist. It
  does not require or control Apollo, and it does not rewrite machine-wide SudoVDA settings.
- The lease-owned SudoVDA handle performs display add, display remove, watchdog query, and
  heartbeat operations. Add or remove must not open a second short-lived control handle:
  the driver's watchdog state is associated with the handle that created the display.
- DisplayConfig work runs on a fresh thread bound to the current Windows input desktop. This
  preserves CCD access when Windows switches from `Default` to `Screen-saver` without weakening
  secure-desktop boundaries.
- Removing a lease whose driver monitor is already absent is idempotent. Win32 `1168`
  (`ERROR_NOT_FOUND`) still releases Beacon's lease and heartbeat ownership.
- `dotnet build Beacon.slnx -warnaserror` succeeds.
- Visual Studio and WDK are only needed for driver rebuild work, not for running the Beacon display probe.

## Probe Commands

Read-only status:

```powershell
dotnet run --project src/Beacon.DisplayProbe -- status
```

Native driver-session validation without creating or changing a display:

```powershell
dotnet run --project src/Beacon.DisplayProbe -- driver-session
```

This opens Beacon's own SudoVDA control session, queries the watchdog, sends an immediate
heartbeat, reports the session state, and releases it. It does not query or control Apollo.

Topology-changing commands:

```powershell
dotnet run --project src/Beacon.DisplayProbe -- ensure --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src/Beacon.DisplayProbe -- primary --client z-fold-7
dotnet run --project src/Beacon.DisplayProbe -- restore-physical
dotnet run --project src/Beacon.DisplayProbe -- remove --client z-fold-7
```

Use `restore-physical` before `remove` when recovering from an active virtual-primary topology. If the legacy display enumeration cannot find the physical panel while the virtual display is primary, Beacon falls back to DisplayConfig path data and restores the non-origin physical candidate rather than failing immediately.

`restore-physical` uses the verified backend path, not the raw one-shot API call. The command can fail even after Windows accepts the DisplayConfig apply if the follow-up topology query still shows a virtual primary or no physical primary.

## Server Composition

Beacon Server has one production composition:

```powershell
dotnet run --project src\Beacon.Server -- --urls https://127.0.0.1:5001
```

It always registers `WindowsDisplayBackend`, `WindowsGameLauncher`,
`WindowsSessionActivityInspector`, `WindowsClientInputSink`, `WindowsRecoveryBackend`,
and `StreamWorkerStreamingBackend`. `/admin/snapshot` exposes the active boundary names.
There is no host or streaming mode selector.

Deterministic API and fake-client tests run a separate test-only executable:

```powershell
dotnet run --project tests\Beacon.Server.TestHost -- --urls http://127.0.0.1:5000 --Beacon:Security:TestHost=true
```

The fake runtime and seeded fixtures are compiled only from `tests/`; they cannot be
selected in the shipped server.

## Manual Recovery Actions

The production server registers `WindowsRecoveryBackend` for explicit local-admin recovery. These endpoints are manual actions:

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
