# Milestone 47 - Keyboard Input

## Goal

Add a conservative keyboard input slice to the existing client input pipeline.

## Requirements Covered

- `REQ-CTRL-009`: The client forwards input according to the active server-owned session plan.
- `REQ-REC-008`: Input capability and failures remain visible through diagnostics and health surfaces.
- `REQ-TEST-007`: Input behavior remains interface-backed and fake-testable.
- `REQ-TEST-010`: Real phone testing remains final confirmation for touch/input feel.

## Implementation

1. Add failing tests for keyboard input health metadata, server forwarding, Cockpit parsing, Windows sink command generation, and Windows virtual-key mapping.
2. Extend `ClientInputHealth` with supported keyboard actions.
3. Teach fake/no-op and Windows input sinks to report `keyboard` event support with `down`, `up`, and `press` actions.
4. Add `WindowsInputCommandKind.KeyboardKey` and map a conservative keyboard subset through Win32 `SendInput`.
5. Update Cockpit to display supported keyboard actions.
6. Update README and the extraction map.

## Scope Boundaries

- Keyboard event type only.
- Keyboard actions are `down`, `up`, and `press`.
- Supported keys are a conservative virtual-key subset: letters, digits, numpad digits, arrows, Escape, Space, Enter, Tab, Backspace, Delete, navigation keys, modifiers, Windows key, and F1-F12.
- No text composition, IME, controller, native touch, multitouch, Android keyboard UI, or GameStream-native input protocol.
- No copied upstream source.
- No timeout-based input behavior.

## Validation

Focused validation:

```powershell
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter "WindowsClientInputSinkTests|WindowsInputApiTests"
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "SnapshotReturnsClientsGamesAndSessions|BeaconServiceRegistrationTests|ClientInputForwardsKeyboardEventToActiveStreamSession"
dotnet test tests\Beacon.Cockpit.Tests\Beacon.Cockpit.Tests.csproj --filter "CockpitApiClientTests|CockpitShellViewModelTests"
```

Full validation is the standard Beacon loop: format, diff check, timeout-pattern audit, .NET build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status.
