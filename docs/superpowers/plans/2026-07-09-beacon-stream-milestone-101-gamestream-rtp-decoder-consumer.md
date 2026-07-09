# Milestone 101: GameStream RTP Decoder Consumer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a fakeable Android GameStream RTP video consumer that can hand RTP-derived encoded samples to the existing MediaCodec surface decoder path when the GameStream connection advertises codec and display metadata.

**Architecture:** Preserve the existing `GameStreamRtpVideoSessionClient` hook and add lifecycle support for stopping a consumer-owned decoder. Extend `GameStreamEndpointPlan` to retain connection metadata. Add a `EncodedVideoStreamPlan.fromGameStreamRtp(...)` factory that converts GameStream RTP metadata into the decoder-compatible plan type. Add `GameStreamRtpDecoderVideoConsumer`, which wraps `SurfaceEncodedVideoDecoder` with a one-shot sample provider factory. This milestone does not implement RTP payload depacketization, SPS/PPS extraction, dynamic port leasing, HDR metadata, or default APK wiring to live GameStream decode.

**Tech Stack:** Java Android stream boundaries, JVM unit tests with fake codecs/surfaces/sample providers, README/extraction-map documentation, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED GameStream RTP Metadata Plan Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/EncodedVideoStreamPlanTest.java`

- [x] **Step 1: Add complete GameStream RTP metadata test**

Create a GameStream `StreamConnectionDescriptor` with `metadata` values `codec=h264`, `container=annex-b`, `width=2560`, `height=1600`, and `fps=120`. Convert it to `GameStreamEndpointPlan`, then call `EncodedVideoStreamPlan.fromGameStreamRtp(plan)`. Assert the result is supported, complete, uses video URI `udp://127.0.0.1:47998`, and exposes the metadata fields.

- [x] **Step 2: Add missing GameStream RTP metadata diagnostic test**

Create a GameStream descriptor with the required endpoints but missing codec/container/width/height/fps metadata. Assert the plan is supported but incomplete with diagnostic `GameStream RTP video metadata is incomplete. Missing or invalid: codec, container, width, height, fps.`

- [x] **Step 3: Run RED metadata tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.EncodedVideoStreamPlanTest
```

Expected: fail because `EncodedVideoStreamPlan.fromGameStreamRtp(...)` and `GameStreamEndpointPlan.videoUri()/metadataValue(...)` do not exist.

### Task 2: GREEN GameStream RTP Metadata Plan

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamEndpointPlan.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoStreamPlan.java`

- [x] **Step 1: Retain endpoint metadata in `GameStreamEndpointPlan`**

Store a defensive metadata map from `StreamConnectionDescriptor.metadata()`. Add `metadataValue(String key)` and `videoUri()` getters.

- [x] **Step 2: Add `EncodedVideoStreamPlan.fromGameStreamRtp(...)`**

Support GameStream/Moonlight endpoint plans by requiring valid codec, container, width, height, and fps metadata. Use the video endpoint URI as `videoUri`. Keep sample transport empty for live RTP.

- [x] **Step 3: Run GREEN metadata tests**

Run the focused `EncodedVideoStreamPlanTest` command again and confirm it passes.

### Task 3: RED Decoder Consumer Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtpDecoderVideoConsumerTest.java`

- [x] **Step 1: Add decoder startup test**

Use fake `EncodedVideoCodecFactory`, `EncodedVideoSurfaceProvider`, and `EncodedVideoSampleProvider`. Start `GameStreamRtpDecoderVideoConsumer` with a complete GameStream plan and session info. Assert it returns success, configures codec `h264`, passes the surface, builds a 2560x1600@120 plan, and gives the codec the exact RTP sample provider.

- [x] **Step 2: Add incomplete metadata diagnostic test**

Start the consumer with a GameStream plan missing codec/container/width/height/fps metadata. Assert it returns unsupported diagnostic `GameStream RTP video metadata is incomplete. Missing or invalid: codec, container, width, height, fps.`

- [x] **Step 3: Add stop releases active codec test**

After successful start, call `stop()` twice and assert the fake codec is stopped and released once.

- [x] **Step 4: Run RED decoder consumer tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtpDecoderVideoConsumerTest
```

Expected: fail because `GameStreamRtpDecoderVideoConsumer` does not exist and `GameStreamRtpVideoConsumer` does not expose stop lifecycle yet.

### Task 4: GREEN Decoder Consumer And Lifecycle

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoConsumer.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoSessionClient.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpDecoderVideoConsumer.java`

- [x] **Step 1: Add consumer stop lifecycle**

Add a default `stop()` method to `GameStreamRtpVideoConsumer`. Update `GameStreamRtpVideoSessionClient` to call `consumer.stop()` before replacing or stopping a successful active source, while still closing the RTP source.

- [x] **Step 2: Add decoder consumer**

Implement `GameStreamRtpDecoderVideoConsumer` with `EncodedVideoCodecFactory` and `EncodedVideoSurfaceProvider`. On start, build `EncodedVideoStreamPlan.fromGameStreamRtp(plan)`, reject incomplete plans, create a `SurfaceEncodedVideoDecoder` with a sample provider factory that returns the provided RTP sample provider, and translate `EncodedVideoDecodeResult` to `NativeStreamStartResult`.

- [x] **Step 3: Run GREEN decoder consumer tests**

Run the focused decoder consumer tests and confirm they pass.

- [x] **Step 4: Run focused session-client tests**

Run `GameStreamRtpVideoSessionClientTest` to confirm the default no-op consumer stop keeps existing tests passing.

### Task 5: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-101-gamestream-rtp-decoder-consumer.md`

- [x] **Step 1: Update docs**

Document that Milestone 101 adds the GameStream RTP decoder consumer boundary, with live depacketization and default APK wiring still pending.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout/connect-timeout patterns.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk
adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity
```

Expected: Android tests/APK build, .NET solution tests, and emulator launch smoke pass.

- [ ] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
