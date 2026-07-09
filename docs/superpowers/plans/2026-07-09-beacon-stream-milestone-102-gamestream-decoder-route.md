# Milestone 102: GameStream Decoder Route Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire Android's default GameStream route to the UDP RTP sample-provider and MediaCodec decoder consumer when a GameStream/Moonlight descriptor explicitly advertises decoder metadata, while preserving RTSP-only behavior for ordinary descriptors without that metadata.

**Architecture:** Keep `GameStreamNativeStreamClient` responsible for RTSP session ownership and the optional video-session hook. Add a metadata gate there: no codec/container/width/height/fps metadata means the configured video hook is skipped and the route remains RTSP-only; any advertised RTP decoder metadata means the video hook owns success or a truthful failure. Extend `AndroidNativeStreamClientFactory` with a video-session overload and a real APK `socketRtpVideoSessionClient(...)` composer that combines `GameStreamUdpRtpPacketSourceFactory` and `GameStreamRtpDecoderVideoConsumer`. Update `GameStreamRtpDecoderVideoConsumer` to return an `encoded-video` presentation so `BeaconActivity` makes the shared `SurfaceView` visible. This milestone still does not implement GameStream-specific RTP depacketization, dynamic client port leasing, audio/control RTP, HDR metadata, or controller protocol support.

**Tech Stack:** Java Android stream composition, JVM unit tests with fake RTSP/video/decoder collaborators, README/extraction-map documentation, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED Native Route Metadata-Gate Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamNativeStreamClientTest.java`

- [x] **Step 1: Add no-metadata RTSP-only preservation test**

Build `GameStreamNativeStreamClient` with a successful RTSP client and a recording video-session client. Start a complete GameStream endpoint map with no `codec`, `container`, `width`, `height`, or `fps` metadata. Assert the route succeeds with the existing RTSP-only status, the video client is not started, and the RTSP client is not stopped.

- [x] **Step 2: Add partial-metadata video failure test**

Build the same client but return `NativeStreamStartResult.unsupported("metadata rejected")` from the recording video client. Start a complete GameStream endpoint map with only `metadata.codec=h264`. Assert the video client is started once, the final diagnostic is `metadata rejected`, and RTSP is stopped once.

- [x] **Step 3: Run RED native route tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamNativeStreamClientTest
```

Expected: fail because a configured video hook is currently attempted for all complete GameStream endpoint maps, even when no decoder metadata was advertised, and the fake recording video client does not expose a start count yet.

### Task 2: GREEN Native Route Metadata Gate

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamNativeStreamClient.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamNativeStreamClientTest.java`

- [x] **Step 1: Track recording video starts in tests**

Add `startCount` to `RecordingVideoSessionClient` and increment it at the start of `start(...)`.

- [x] **Step 2: Gate optional video startup by advertised RTP decoder metadata**

In `GameStreamNativeStreamClient`, call the video session only when at least one GameStream RTP decoder metadata key is present. If none are present, keep the existing RTSP-only success path. If any are present, keep the current `startVideoSession(...)` flow so incomplete metadata is surfaced by the video path and RTSP is released on failure.

- [x] **Step 3: Run GREEN native route tests**

Run the focused `GameStreamNativeStreamClientTest` command again and confirm it passes.

### Task 3: RED Factory And Presentation Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/AndroidNativeStreamClientFactoryTest.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtpDecoderVideoConsumerTest.java`

- [x] **Step 1: Add factory video-session route test**

Add an overload-facing test that builds `AndroidNativeStreamClientFactory.create(encodedDecoder, rtspClient, videoClient)`, starts a GameStream descriptor with complete RTP decoder metadata, and asserts the injected RTSP and video clients both start.

- [x] **Step 2: Add decoder consumer presentation assertion**

Extend `startsDecoderWithRtpSampleProvider()` to assert `result.presentation().active()`, kind `encoded-video`, endpoint URI `udp://127.0.0.1:47998`, and a label containing the codec/resolution/fps.

- [x] **Step 3: Run RED factory and decoder tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.AndroidNativeStreamClientFactoryTest --tests dev.beacon.android.GameStreamRtpDecoderVideoConsumerTest
```

Expected: fail because the factory overload and decoder presentation are not implemented yet.

### Task 4: GREEN Factory Route And Activity Composition

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidNativeStreamClientFactory.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpDecoderVideoConsumer.java`

- [x] **Step 1: Add factory overload and real video-session composer**

Add `create(EncodedVideoDecoder, GameStreamRtspSessionClient, GameStreamVideoSessionClient)` and have the existing two-argument overload call it with `null`. Add `socketRtpVideoSessionClient(EncodedVideoCodecFactory, EncodedVideoSurfaceProvider)` returning `new GameStreamRtpVideoSessionClient(new GameStreamUdpRtpPacketSourceFactory(), new GameStreamRtpDecoderVideoConsumer(codecFactory, surfaceProvider))`.

- [x] **Step 2: Wire Activity with shared codec/surface collaborators**

In `BeaconActivity.createNativeStreamClient(...)`, create one `AndroidMediaCodecFactory` and one `AndroidSurfaceViewProvider`, use them for both the existing diagnostic encoded-video decoder and the new GameStream RTP video session client, then call the three-argument factory overload.

- [x] **Step 3: Return encoded-video presentation from GameStream RTP decoder consumer**

On successful decoder start, return `NativeStreamStartResult.started(result.status(), NativeStreamPresentation.encodedVideo(videoPlan.videoUri(), videoPlan.codec(), videoPlan.width(), videoPlan.height(), videoPlan.fps()))`.

- [x] **Step 4: Run GREEN factory and decoder tests**

Run the focused factory and decoder test command again and confirm it passes.

### Task 5: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-102-gamestream-decoder-route.md`

- [x] **Step 1: Update docs**

Document that Milestone 102 wires the default APK route to the GameStream RTP decoder path only when decoder metadata is advertised, with live depacketization and dynamic port leasing still pending.

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
