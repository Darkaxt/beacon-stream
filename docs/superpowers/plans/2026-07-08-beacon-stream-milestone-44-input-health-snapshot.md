# Milestone 44 - Input Health Snapshot

## Goal

Expose the active input backend and its supported input surface through `/admin/snapshot` and Cockpit diagnostics so fake no-op input and Windows `SendInput` are visible before phone testing.

## Requirements Covered

- `REQ-REC-008`: Input boundary readiness and failures must be visible in diagnostics.
- `REQ-TEST-001`: Most validation must work without the real phone.
- `REQ-TEST-007`: Input remains interface-driven and fakeable.
- `REQ-TEST-010`: Real phone testing remains final confirmation for touch/input experience.

## Implementation

1. Add failing tests for admin snapshot `inputHealth`, host DI registration, Cockpit API parsing, and Cockpit diagnostics rendering.
2. Add `ClientInputHealth` and `IClientInputHealthProvider` to the core input boundary.
3. Make `NoOpClientInputSink` and `WindowsClientInputSink` report their backend and supported pointer actions.
4. Register the active input sink as both `IClientInputSink` and `IClientInputHealthProvider`.
5. Render input health in Cockpit diagnostics without adding another settings surface.
6. Update README documentation.

## Scope Boundaries

- Read-only health reporting only.
- No new input event types.
- No keyboard, controller, native touch, or multitouch expansion.
- No polling, watchdog, or timeout-based input behavior.
- No upstream source copied.

## Validation

Focused validation:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "AdminApiTests|BeaconServiceRegistrationTests"
dotnet test tests\Beacon.Cockpit.Tests\Beacon.Cockpit.Tests.csproj --filter "CockpitApiClientTests|CockpitShellViewModelTests"
```

Full validation is the standard Beacon loop: format, diff check, timeout-pattern audit, .NET build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status.
