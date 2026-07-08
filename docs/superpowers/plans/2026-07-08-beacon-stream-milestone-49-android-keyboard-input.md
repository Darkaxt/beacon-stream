# Milestone 49 - Android Keyboard Input

## Goal

Let the Android APK send a simple Escape keyboard press through the existing client input route.

## Requirements Covered

- `REQ-CTRL-002`: The APK reports user input intent to the server.
- `REQ-CTRL-009`: The APK consumes the active session plan and does not choose display topology locally.
- `REQ-PROFILE-007`: Rich local keyboard/controller/touch UI remains client-local and can evolve separately.
- `REQ-TEST-010`: Real phone testing remains final confirmation for actual input feel.

## Implementation

1. Add a failing JVM test for `InputBatch.keyboardPress`.
2. Extend Android input serialization with `keyboard` / `press` / `Escape` events.
3. Add a simple `Send Escape` APK action that calls the existing `sendInput` flow.
4. Update README documentation.

## Scope Boundaries

- Escape key press only.
- No Android soft-keyboard capture.
- No controller, native touch, multitouch, text composition, IME, or GameStream-native input.
- No server protocol expansion beyond the existing keyboard event contract.
- No display policy in the APK.
- No timeout-based behavior.

## Validation

Focused validation:

```powershell
gradle --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.BeaconApiClientTest.inputSerializesKeyboardPressToOwningClientEndpoint
```

Full validation is the standard Beacon loop: format, diff check, timeout-pattern audit, .NET build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status.
