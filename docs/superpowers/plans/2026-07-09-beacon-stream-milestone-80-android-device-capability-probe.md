# Milestone 80: Android Device Capability Probe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Android's hardcoded codec capability report with a device-derived decoder capability probe so the server planner receives real APK-side facts before native streaming transport work.

**Architecture:** Add a small Android capability-probe boundary with a fakeable codec catalog. JVM tests exercise the pure resolver with fake codec descriptors; production uses `MediaCodecList` through an Android catalog adapter. `BeaconActivity` keeps server display policy out of the APK, but it now reports codec, FPS, low-latency, screen mode, and conservative HDR facts from the device boundary instead of static booleans.

**Tech Stack:** Java Android app, Android `MediaCodecList` adapter, JVM unit tests, existing Beacon capability JSON contract.

---

## Requirements

- `REQ-CTRL-002`: the APK reports identity, facts, capabilities, preferences, telemetry, and user intent.
- `REQ-CTRL-009`: the APK consumes server policy and reports facts without reinterpreting display topology.
- `REQ-HDR-006`: HDR capability must be treated as a chain that includes client decoder/display support.
- `REQ-HDR-008`: missing HDR capability must be reported truthfully rather than forced.
- `REQ-NET-001`: the server estimates endpoint capabilities before launch when facts are available.
- `REQ-NET-002`: the client reports decode capability and current screen mode when known.
- `REQ-NET-003`: the server chooses codec/FPS/bitrate/transport from profile plus live endpoint facts.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-TEST-010`: real phone testing remains final confirmation for decoder compatibility.
- `REQ-SYNC-006`: docs, implementation, and validation status stay aligned.

## Non-Goals

- No real video decode.
- No GameStream/Moonlight transport implementation.
- No controller protocol.
- No native touch protocol.
- No server-side display policy in the APK.

## Files

- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidCodecCatalog.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidCodecDescriptor.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidMediaCodecCatalog.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidDeviceCapabilityProbe.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/AndroidDeviceCapabilityProbeTest.java`
- Modify: `README.md`

## Tasks

### Task 1: RED capability-probe tests

- [x] **Step 1: Write failing JVM tests**

Add `AndroidDeviceCapabilityProbeTest` proving:

- decoder MIME types `video/avc`, `video/hevc`, and `video/av01` map to H.264, HEVC, and AV1 support;
- encoders do not count as decoders;
- low-latency support is true when any supported decoder reports it;
- HDR10 is only true when a supported decoder reports HDR and the screen HDR flag is true;
- `maxFps` and `currentScreenMode` come from the current UI/device mode.

- [x] **Step 2: Run RED tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.AndroidDeviceCapabilityProbeTest
```

Expected: compile failure because the probe classes do not exist.

### Task 2: GREEN capability probe

- [x] **Step 1: Implement pure catalog boundary**

Add:

- `AndroidCodecCatalog` with `codecs()`;
- `AndroidCodecDescriptor` with encoder flag, supported MIME types, low-latency support, and HDR10 support;
- `AndroidDeviceCapabilityProbe` with `read(width, height, refreshHz, screenHdr10Supported)`.

- [x] **Step 2: Implement Android MediaCodec adapter**

Add `AndroidMediaCodecCatalog` that reads `MediaCodecList.ALL_CODECS`, ignores failing codec capability reads, and marks low-latency/HDR10 conservatively.

- [x] **Step 3: Run GREEN tests**

Run the Task 1 Gradle command again. Expected: PASS.

### Task 3: Wire Activity and docs

- [x] **Step 1: Wire `BeaconActivity.readCapabilities()`**

Add a `AndroidDeviceCapabilityProbe` field initialized from `AndroidDeviceCapabilityProbe.system()`. Use it in `readCapabilities()` with the current preferred width/height/refresh and screen HDR support. Keep virtual display HDR reporting conservative until the server-side display chain proves it.

- [x] **Step 2: Update README**

Document that Android now reports device-derived decoder facts and current screen mode, while real phone testing is still required for actual decoder compatibility and stream quality.

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

- [ ] **Step 2: Commit and PR**

Commit with:

```powershell
git add README.md docs\superpowers\plans\2026-07-09-beacon-stream-milestone-80-android-device-capability-probe.md src\Beacon.Android\app\src\main\java\dev\beacon\android\AndroidCodecCatalog.java src\Beacon.Android\app\src\main\java\dev\beacon\android\AndroidCodecDescriptor.java src\Beacon.Android\app\src\main\java\dev\beacon\android\AndroidMediaCodecCatalog.java src\Beacon.Android\app\src\main\java\dev\beacon\android\AndroidDeviceCapabilityProbe.java src\Beacon.Android\app\src\main\java\dev\beacon\android\BeaconActivity.java src\Beacon.Android\app\src\test\java\dev\beacon\android\AndroidDeviceCapabilityProbeTest.java
git commit -m "Report Android device decoder capabilities"
```

Push, open a PR, verify CI, and merge when green.
