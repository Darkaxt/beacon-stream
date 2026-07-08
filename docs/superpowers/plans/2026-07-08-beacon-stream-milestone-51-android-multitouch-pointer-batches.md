# Milestone 51 - Android Multitouch Pointer Batches

## Goal

Let the Android touch surface forward multiple active touch points in one client input batch through the existing pointer input route.

## Requirements Covered

- `REQ-CTRL-002`: The APK reports user input intent to the server.
- `REQ-CTRL-006`: Touch behavior remains client-local and does not change server display policy.
- `REQ-CTRL-009`: The APK forwards input against the server-owned active session instead of choosing topology.
- `REQ-PROFILE-007`: Multitouch behavior remains an APK-local interaction concern.
- `REQ-TEST-001`: The mapper behavior is testable without the real phone.

## Implementation

1. Add failing JVM tests for multi-pointer touch-to-pointer batches.
2. Add `BeaconTouchInputMapper.mapPointers` and keep the single-pointer mapper as a delegate.
3. Update `BeaconActivity` so secondary pointer down/up events are forwarded and move/cancel can send all active pointers in one batch.
4. Update README capability and future-work wording.

## Scope Boundaries

- Uses the existing `pointer` input event type and `/clients/{clientId}/input` route.
- No server protocol expansion.
- No display policy in the APK.
- No native Android touch protocol, gesture recognizer, haptics, controller, IME, text composition, or GameStream-native input work.
- No timeout-based behavior.

## Validation

Focused red-green validation:

```powershell
gradle --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.BeaconTouchInputMapperTest
```

APK compile validation:

```powershell
gradle --no-daemon -p src\Beacon.Android test assembleDebug
```

Full validation is the standard Beacon loop: format, diff check, timeout-pattern audit, .NET build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status.
