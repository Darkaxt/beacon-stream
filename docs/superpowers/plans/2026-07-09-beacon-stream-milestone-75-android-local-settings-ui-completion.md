# Android Local Settings UI Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the thin Android APK edit and apply all version 1 client-local settings already modeled in `BeaconLocalSettings`.

**Architecture:** Keep all touch layout, multitouch, controller overlay, haptics, UI density, theme, wake lock, and decoder overlay state inside the APK. Extend the pure Java form/state helpers first, then wire the Activity controls to those helpers without adding server display policy or global settings.

**Tech Stack:** Android Java, JVM unit tests, existing programmatic Activity UI.

---

## Requirements

- `REQ-PROFILE-007`: the APK stores touch layout, multitouch gestures, controller overlay, haptics, local UI density, wake lock, local theme, and local decoder UI preferences locally.
- `REQ-CTRL-006`: client-local input and UI settings stay on the client.
- `REQ-CTRL-007`: virtual desktop behavior remains server-side state.
- `REQ-TEST-001`: validation must not require the real phone.
- `REQ-SYNC-006`: docs, implementation, and validation status stay aligned.

## Non-Goals

- No native video decode.
- No Moonlight/GameStream protocol work.
- No server profile or display-policy edits.
- No haptic timing loops or timeout-based behavior.
- No real controller protocol implementation.

## File Map

- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconLocalSettingsForm.java`
  - Update all modeled client-local settings and normalize enum-like strings.
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconLocalSettingsUiState.java`
  - Expose settings needed by Activity rendering: multitouch, controller overlay, haptics, density, touch layout, text sizes, padding, and touch-surface height.
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
  - Add controls for touch layout, UI density, multitouch, controller overlay, and haptics.
  - Apply UI density/touch layout to the programmatic UI.
  - Respect `multitouchEnabled` when mapping touch batches.
  - Trigger Android haptic feedback on touch down only when `hapticsEnabled` is true.
  - Show/hide a simple controller overlay marker from local state.
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconLocalSettingsFormTest.java`
  - Add RED tests for full local-settings update and normalization.
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconLocalSettingsUiStateTest.java`
  - Add RED tests for local input/UI flags and density-derived values.
- Modify: `README.md`
  - Update Android client wording to say all version 1 client-local settings are editable locally.

## Task 1: RED form tests

- [x] Add `updateAppliesAllClientLocalSettings` to `BeaconLocalSettingsFormTest`.
- [x] Call `BeaconLocalSettingsForm.update(existing, "edge", false, false, false, "dense", "light", false, true)`.
- [x] Assert touch layout, multitouch, controller overlay, haptics, UI density, theme, wake lock, and decoder overlay all changed.
- [x] Assert the serialized JSON still does not contain server display-policy fields such as `displayMode`, `blackout`, or `restorePhysicalDisplayOnEnd`.
- [x] Add `invalidLocalSettingValuesFallBackToDefaults`.
- [x] Call `BeaconLocalSettingsForm.update(null, "unsupported-layout", true, true, true, "unsupported-density", "unsupported-theme", true, false)`.
- [x] Assert layout is `default`, density is `comfortable`, and theme is `system`.
- [x] Run:

```powershell
& 'C:\Users\darka\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat' --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.BeaconLocalSettingsFormTest
```

Expected: fail because `BeaconLocalSettingsForm.update` does not yet accept the expanded local settings.

## Task 2: GREEN form implementation

- [x] Extend `BeaconLocalSettingsForm.update` with parameters:

```java
String touchLayout,
boolean multitouchEnabled,
boolean controllerOverlayEnabled,
boolean hapticsEnabled,
String uiDensity,
String localTheme,
boolean wakeLockEnabled,
boolean decoderDebugOverlayEnabled
```

- [x] Add `normalizeTouchLayout` accepting only `default`, `compact`, and `edge`.
- [x] Add `normalizeUiDensity` accepting only `comfortable`, `dense`, and `large`.
- [x] Keep `normalizeTheme` accepting only `system`, `dark`, and `light`.
- [x] Update existing call sites and tests.
- [x] Run the same focused Gradle command.

Expected: pass.

## Task 3: RED UI state tests

- [x] Add `localInputFlagsAndDenseLayoutAreExposed` to `BeaconLocalSettingsUiStateTest`.
- [x] Configure settings with `touchLayout=edge`, `multitouchEnabled=false`, `controllerOverlayEnabled=false`, `hapticsEnabled=false`, and `uiDensity=dense`.
- [x] Assert the state exposes those flags and uses smaller padding/text metrics than default.
- [x] Add `largeDensityUsesLargerMetrics`.
- [x] Assert large density uses larger title/body/padding/touch-surface values than dense.
- [x] Run:

```powershell
& 'C:\Users\darka\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat' --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.BeaconLocalSettingsUiStateTest
```

Expected: fail because `BeaconLocalSettingsUiState` does not expose those fields or metrics.

## Task 4: GREEN UI state and Activity wiring

- [x] Add fields and accessors to `BeaconLocalSettingsUiState` for local input flags, `touchLayout`, `uiDensity`, `contentPaddingPx`, `titleTextSizeSp`, `bodyTextSizeSp`, and `touchSurfaceMinHeightPx`.
- [x] Add `TOUCH_LAYOUT_VALUES` and `UI_DENSITY_VALUES` spinners to `BeaconActivity`.
- [x] Add checkboxes for multitouch, controller overlay, and haptics.
- [x] Update `saveLocalSettings` to call the expanded `BeaconLocalSettingsForm.update`.
- [x] Apply density metrics in `createContent`, `text`, and `touchSurface`.
- [x] If multitouch is disabled and `MotionEvent.getPointerCount() > 1`, map only the action pointer instead of all pointers.
- [x] If haptics are enabled, call `performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY)` on pointer-down actions.
- [x] Show/hide a simple controller overlay `TextView` using `controllerOverlayEnabled`.
- [x] Run the focused UI state Gradle command again.

Expected: pass.

## Task 5: Docs and validation

- [x] Update README Android wording.
- [x] Run:

```powershell
git diff --check
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
& 'C:\Users\darka\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat' --no-daemon -p src\Beacon.Android test assembleDebug
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
dotnet test Beacon.slnx --no-build
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir src\Beacon.ClientLab build
pnpm --dir tests\Beacon.ClientLab.Playwright test
dotnet run --project src\Beacon.DisplayProbe -- status
```

- [ ] Commit with message `Complete Android local settings controls`.
- [ ] Push `codex/milestone-75-android-local-settings-ui-completion`.
- [ ] Open a PR with validation evidence.
- [ ] Watch CI and merge when green.
