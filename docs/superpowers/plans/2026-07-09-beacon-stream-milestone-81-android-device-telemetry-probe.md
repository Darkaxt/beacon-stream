# Milestone 81: Android Device Telemetry Probe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enrich Android telemetry reports with device-derived battery, thermal, and Wi-Fi transport facts while preserving manually supplied network/decode sample values for emulator testing.

**Architecture:** Add a small fakeable telemetry source boundary. The pure `AndroidDeviceTelemetryProbe` merges manually entered RTT, packet loss, decoder load, and bandwidth with device facts from `AndroidDeviceTelemetrySource`. Production uses `AndroidSystemTelemetrySource`, which reads BatteryManager, PowerManager thermal status when available, and ConnectivityManager Wi-Fi transport conservatively. Missing system facts do not invent values.

**Tech Stack:** Java Android app, Android system service adapters, JVM unit tests.

---

## Requirements

- `REQ-CTRL-002`: the APK reports identity, facts, capabilities, preferences, telemetry, and user intent.
- `REQ-NET-001`: the server estimates endpoint capabilities, endpoint load, and network quality before launch when facts are available.
- `REQ-NET-002`: the client reports live telemetry such as RTT, packet loss estimate, Wi-Fi band if available, decode capability, current screen mode, battery, thermal hints, and local decoder load if known.
- `REQ-NET-003`: the server chooses codec, FPS, bitrate, transport, and congestion policy from profile plus live telemetry.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-TEST-010`: real phone testing remains final confirmation for real telemetry quality.
- `REQ-SYNC-006`: docs, implementation, and validation status stay aligned.

## Non-Goals

- No active network probing or ping loop.
- No packet-loss measurement implementation.
- No hard timeout-based telemetry collection.
- No server display policy in the APK.
- No real decoder-load measurement beyond the existing sample field.

## Files

- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidDeviceTelemetry.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidDeviceTelemetrySource.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidDeviceTelemetryProbe.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidSystemTelemetrySource.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
- Modify: `src/Beacon.Android/app/src/main/AndroidManifest.xml`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/AndroidDeviceTelemetryProbeTest.java`
- Modify: `README.md`

## Tasks

### Task 1: RED telemetry probe tests

- [x] **Step 1: Write failing JVM tests**

Add `AndroidDeviceTelemetryProbeTest` proving:

- manual RTT, packet loss, decoder load, and bandwidth are preserved;
- device battery percent, thermal state, and Wi-Fi band override blank/default UI values;
- UI fallback values are used when device facts are missing;
- invalid negative battery values are ignored;
- blank thermal/Wi-Fi values are not serialized as invented facts.

- [x] **Step 2: Run RED tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.AndroidDeviceTelemetryProbeTest
```

Expected: compile failure because the telemetry probe classes do not exist.

Observed: `testDebugUnitTest --tests dev.beacon.android.AndroidDeviceTelemetryProbeTest` failed at compile time because `AndroidDeviceTelemetrySource`, `AndroidDeviceTelemetry`, and `AndroidDeviceTelemetryProbe` were missing.

### Task 2: GREEN telemetry probe

- [x] **Step 1: Implement pure telemetry classes**

Add:

- `AndroidDeviceTelemetry` with nullable `batteryPercent`, `thermalState`, and `wifiBand`;
- `AndroidDeviceTelemetrySource` with `read()`;
- `AndroidDeviceTelemetryProbe` with `read(rttMs, packetLossPercent, decoderLoadPercent, estimatedBandwidthMbps, fallbackWifiBand, fallbackBatteryPercent, fallbackThermalState)`.

The probe must preserve the existing manual/network sample fields and only enrich optional device fields when valid.

- [x] **Step 2: Run GREEN tests**

Run the Task 1 Gradle command again. Expected: PASS.

Observed: focused `AndroidDeviceTelemetryProbeTest` passed.

### Task 3: Android system source and Activity wiring

- [x] **Step 1: Implement `AndroidSystemTelemetrySource`**

Read:

- `BatteryManager.BATTERY_PROPERTY_CAPACITY`, ignoring values outside `1..100`;
- `PowerManager.getCurrentThermalStatus()` on Android Q and later, mapping statuses to `none`, `light`, `moderate`, `hot`, or `critical`;
- `ConnectivityManager` active network Wi-Fi transport, returning `wifi` when available.

- [x] **Step 2: Wire `BeaconActivity.readTelemetry()`**

Add a `AndroidDeviceTelemetryProbe` field initialized from `AndroidDeviceTelemetryProbe.system(this)`. Use it in `readTelemetry()` with existing UI values as fallbacks.

- [x] **Step 3: Update README**

Document that Android telemetry now enriches manual RTT/packet-loss/decode samples with device battery, thermal, and Wi-Fi transport facts.

### Task 4: Validation and sync

- [x] **Step 1: Validate**

Run:

```powershell
git diff --check
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
dotnet test Beacon.slnx --no-build
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: all commands pass; `rg` should return no matches.

Observed: `git diff --check`, the timeout/cancellation search, `dotnet test Beacon.slnx --no-build`, and Android `test assembleDebug` all passed.

- [ ] **Step 2: Commit and PR**

Commit with:

```powershell
git add README.md docs\superpowers\plans\2026-07-09-beacon-stream-milestone-81-android-device-telemetry-probe.md src\Beacon.Android\app\src\main\java\dev\beacon\android\AndroidDeviceTelemetry.java src\Beacon.Android\app\src\main\java\dev\beacon\android\AndroidDeviceTelemetrySource.java src\Beacon.Android\app\src\main\java\dev\beacon\android\AndroidDeviceTelemetryProbe.java src\Beacon.Android\app\src\main\java\dev\beacon\android\AndroidSystemTelemetrySource.java src\Beacon.Android\app\src\main\java\dev\beacon\android\BeaconActivity.java src\Beacon.Android\app\src\test\java\dev\beacon\android\AndroidDeviceTelemetryProbeTest.java
git commit -m "Report Android device telemetry facts"
```

Push, open a PR, verify CI, and merge when green.
