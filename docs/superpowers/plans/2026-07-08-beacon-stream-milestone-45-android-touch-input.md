# Milestone 45 - Android Touch Input

## Goal

Move the Android shell from a fixed debug input button toward real APK input forwarding by adding a JVM-tested touch-to-pointer mapper and a simple single-pointer touch surface.

## Requirements Covered

- `REQ-CTRL-009`: The client forwards input against the server-computed session and does not choose display topology locally.
- `REQ-CTRL-006`: Client-local input UI remains on the APK side.
- `REQ-TEST-001`: Input behavior remains testable without the real phone.
- `REQ-TEST-010`: Real phone testing remains final confirmation for touch feel and stream experience.

## Implementation

1. Add failing JVM tests for Android touch down/move/up mapping, normalized coordinate clamping, and invalid surface geometry.
2. Add `BeaconTouchInputMapper` as a pure Java class that emits `BeaconApiClient.InputBatch`.
3. Allow pointer move events to omit the button mask while preserving button masks for down/up/tap.
4. Add a simple Activity touch surface that maps Android `MotionEvent` down/move/up/cancel into the existing `BeaconViewModel.sendInput` route.
5. Update README and extraction map.

## Scope Boundaries

- Single-pointer pointer events only.
- No native Android touch protocol.
- No multitouch gestures.
- No controller, keyboard, haptics, or overlay work.
- No stream/decode implementation.
- No timeout-based input handling.
- No upstream source copied.

## Validation

Focused validation:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.BeaconTouchInputMapperTest
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Full validation is the standard Beacon loop: format, diff check, timeout-pattern audit, .NET build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status.
