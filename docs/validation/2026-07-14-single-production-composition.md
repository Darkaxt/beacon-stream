# Single Production Composition Validation

Date: 2026-07-14

## Boundary

Beacon Server now has one shipped composition: Windows display, game launch,
session activity, input, recovery, installed-game discovery, and Beacon StreamWorker.
The `Beacon:HostMode`, `BEACON_HOST_MODE`, `Beacon:StreamingMode`, and
`BEACON_STREAMING_MODE` selectors were removed. Production appsettings no longer
default to a fake host.

Deterministic display, launch, recovery, input, authorization, benchmark, and
streaming doubles moved from `src/` to `tests/Beacon.Testing`. The process-level
simulator uses `tests/Beacon.Server.TestHost`; it replaces production boundaries
through test-owned dependency injection and cannot be selected by the shipped server.

The hard-coded Dispatch catalog was also removed from production. Server and GameProbe
now share `WindowsGameLibraryProviderFactory`, which discovers Steam applications and
shortcuts, Heroic, Hydra, and Beacon manual entries. The live read-only probe found
36 games on this host.

## Red Evidence

- `ProductionCompositionHasOneWindowsWorkerRuntime` failed on the shipped
  `BeaconStreamingMode` file and the no-op/static registrations.
- `TestDoublesAreNotCompiledIntoProductionProjects` failed on all six fake source files
  plus the embedded no-op input and fake authorizer classes.

## Green Evidence

- `dotnet format Beacon.slnx --verify-no-changes --no-restore`
- `dotnet build Beacon.slnx --no-restore -warnaserror`: zero warnings and errors
- `dotnet test Beacon.slnx --no-build`: 524 tests
- `scripts/test-gate3.ps1 -ValidateFixtures`
- Client Lab lint and 16 unit tests
- Client Lab Playwright lifecycle: 1 test
- Windows native clean configure/build and 23 CTest cases
- Worker process integration: real H.264 access unit, QUIC auth/input/feedback,
  network benchmark, typed video failure, and startup failure
- Android unit tests plus debug, release, and instrumentation APK builds

No local ADB/emulator, display-topology mutation, Apollo process/configuration, or
external streaming product was used during this validation.
