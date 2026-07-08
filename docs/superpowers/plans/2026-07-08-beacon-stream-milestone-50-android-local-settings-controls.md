# Milestone 50 - Android Local Settings Controls

## Goal

Make a first set of APK-local settings visible and functional in the Android shell without moving display policy into the APK.

## Requirements Covered

- `REQ-CTRL-006`: Client-local input and UI settings stay on the client.
- `REQ-CTRL-007`: Virtual desktop behavior remains server-side state.
- `REQ-PROFILE-007`: The APK stores local theme, wake lock, and decoder/debug UI preferences locally.
- `REQ-TEST-001`: The behavior remains testable without the real phone.

## Implementation

1. Add a pure Java `BeaconLocalSettingsUiState` that maps persisted local settings to theme palette, wake-lock intent, and debug-overlay visibility.
2. Add a pure Java `BeaconLocalSettingsForm` that updates only the local settings controlled by the Activity while preserving other client-local fields.
3. Wire `BeaconActivity` to load `SharedPreferences` local settings on startup.
4. Add Activity controls for theme, wake lock, and decoder debug overlay.
5. Apply `FLAG_KEEP_SCREEN_ON` from the local setting and keep the debug overlay local to the APK.

## Scope Boundaries

- No server profile fields are added.
- No global settings are edited by the APK.
- No display mode, blackout, mirror, persistence, or restore policy moves into Android.
- No controller, native touch, multitouch, real decoder, or GameStream-native input work.
- No timeout-based behavior.

## Validation

Focused red-green validation:

```powershell
gradle --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.BeaconLocalSettingsUiStateTest
gradle --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.BeaconLocalSettingsFormTest
```

APK compile validation:

```powershell
gradle --no-daemon -p src\Beacon.Android assembleDebug
```

Full validation is the standard Beacon loop: format, diff check, timeout-pattern audit, .NET build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status.
