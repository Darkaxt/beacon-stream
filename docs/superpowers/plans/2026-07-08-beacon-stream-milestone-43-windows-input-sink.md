# Milestone 43 - Windows Input Sink

## Goal

Replace the Windows-host no-op input path with a source-audited, display-targeted pointer input sink that can be validated without the phone.

## Requirements Covered

- `REQ-CTRL-009`: The client sends input against the server-computed session plan and does not reinterpret display topology.
- `REQ-REC-008`: Input failures must be observable through the existing input diagnostics added in Milestone 42.
- `REQ-TEST-007`: The native boundary remains interface-driven and fakeable.
- `REQ-TEST-010`: Real phone testing remains final confirmation, not a requirement for this milestone.

## Implementation

1. Add red tests for Windows pointer targeting, missing displays, unsupported input, invalid coordinates, and Windows host DI registration.
2. Add `WindowsClientInputSink`, registered only in Windows host mode.
3. Add `IWindowsInputApi` and `WindowsInputApi` as the only Win32 `SendInput` boundary.
4. Add a source audit against Sunshine, Moonlight Android, Apollo, and Vibeshine.
5. Update README and extraction map.

## Scope Boundaries

- Pointer `move`, `down`, `up`, and `tap` only.
- No copied upstream source.
- No native touch, multitouch, keyboard, or controller injection.
- No streaming protocol implementation.
- No timeout-based input expiry or cancellation.

## Validation

Focused validation:

```powershell
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter WindowsClientInputSinkTests
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter BeaconServiceRegistrationTests
```

Full validation is the standard Beacon loop: format, diff check, timeout-pattern audit, .NET build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status.
