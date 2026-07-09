# Milestone 98: GameStream RTP Video Packet Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an emulator-testable Android RTP video packet boundary behind the GameStream video-session hook, without opening real UDP sockets or claiming full Moonlight/GameStream decode.

**Architecture:** Introduce a small RTP packet parser, a fakeable `RtpPacketSource`, and a `GameStreamRtpVideoSampleProvider` that turns complete encoded RTP payloads into `EncodedVideoSample` values with presentation timestamps derived from the RTP 90 kHz video clock. Add `GameStreamRtpVideoSessionClient` that implements `GameStreamVideoSessionClient`, creates a source through an injected factory, and hands the sample provider to an injected consumer. The real UDP source and MediaCodec consumer wiring remain future milestones.

**Tech Stack:** Java Android client boundaries, JVM unit tests, README/extraction-map documentation, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED RTP Packet Parser Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtpPacketTest.java`

- [x] **Step 1: Add parses basic RTP packet test**

Assert that a byte array with RTP version 2, payload type 96, marker bit set, sequence `0x1234`, timestamp `90000`, SSRC `0x01020304`, and payload `{0x00, 0x00, 0x01, 0x65}` parses into those fields and returns a defensive payload copy.

- [x] **Step 2: Add rejects invalid packet tests**

Assert packets shorter than 12 bytes and packets with a non-v2 RTP version throw `IllegalArgumentException` with clear messages.

- [x] **Step 3: Add skips CSRC and extension header test**

Build a packet with one CSRC, RTP extension bit set, one 32-bit extension word, and payload `{1, 2, 3}`. Assert the parser returns only `{1, 2, 3}` as payload.

- [x] **Step 4: Run RED parser tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.RtpPacketTest
```

Expected: fail because `RtpPacket` does not exist.

Result: the focused Gradle run failed at `:app:compileDebugUnitTestJavaWithJavac` because `RtpPacket` did not exist.

### Task 2: GREEN RTP Packet Parser

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtpPacket.java`

- [x] **Step 1: Implement `RtpPacket`**

Add immutable fields and getters for `marker()`, `payloadType()`, `sequenceNumber()`, `timestamp()`, `ssrc()`, and `payload()`. Implement `parse(byte[] bytes)` for RTP v2 packets, including CSRC and extension header skipping. Throw `IllegalArgumentException` for missing bytes, unsupported version, truncated CSRC list, truncated extension header, or truncated extension payload.

- [x] **Step 2: Run GREEN parser tests**

Run the focused parser tests and confirm they pass.

Result: the focused `RtpPacketTest` run passed.

### Task 3: RED RTP Sample Provider Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtpVideoSampleProviderTest.java`

- [x] **Step 1: Add packet payload to sample mapping test**

Use a fake `RtpPacketSource` with two packets: timestamp `90000` payload `{0, 0, 1, 0x65}` and timestamp `91500` payload `{0, 0, 1, 0x41}`. Assert the first sample PTS is `0`, the second is `16666`, and the third result is EOS.

- [x] **Step 2: Add ignores empty RTP payload test**

Use a fake source returning one empty-payload packet followed by one non-empty packet. Assert the first returned encoded sample contains the non-empty payload and no empty sample is emitted.

- [x] **Step 3: Add source exception becomes sample provider exception test**

Use a fake source whose `nextPacket()` throws `IllegalStateException("udp read failed")`. Assert `nextSample()` throws `IllegalStateException` with the same message.

- [x] **Step 4: Run RED sample provider tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtpVideoSampleProviderTest
```

Expected: fail because `RtpPacketSource` and `GameStreamRtpVideoSampleProvider` do not exist.

Result: the focused Gradle run failed at `:app:compileDebugUnitTestJavaWithJavac` because `RtpPacketSource` and `GameStreamRtpVideoSampleProvider` did not exist.

### Task 4: GREEN RTP Sample Provider

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtpPacketSource.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoSampleProvider.java`

- [x] **Step 1: Add `RtpPacketSource`**

Create an interface with `RtpPacket nextPacket()` and `void close()`. Returning `null` means end-of-stream. Add a static `endOfStreamOnly()` helper.

- [x] **Step 2: Add `GameStreamRtpVideoSampleProvider`**

Implement `EncodedVideoSampleProvider`. Use the first non-empty RTP packet timestamp as the base timestamp. Convert RTP timestamps to microseconds using a 90,000 Hz video clock: `(timestamp - baseTimestamp) * 1_000_000 / 90_000`. Skip empty payloads. Return `EncodedVideoSample.eos()` when the source returns `null`.

- [x] **Step 3: Run GREEN sample provider tests**

Run the focused sample provider tests and confirm they pass.

Result: the focused `GameStreamRtpVideoSampleProviderTest` run passed.

### Task 5: RED RTP Video Session Client Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtpVideoSessionClientTest.java`

- [x] **Step 1: Add starts consumer with packet sample provider test**

Use a recording source factory and recording consumer. Start with a complete GameStream endpoint plan and `GameStreamRtspSessionInfo`. Assert the factory receives both, the consumer receives a `GameStreamRtpVideoSampleProvider`, and the final result is the consumer result.

- [x] **Step 2: Add source factory failure returns diagnostic test**

Make the source factory throw `IllegalStateException("udp source failed")`. Assert start returns unsupported with diagnostic `GameStream RTP video source failed: udp source failed`.

- [x] **Step 3: Add consumer failure closes source test**

Make the consumer return unsupported. Assert the source is closed once and the consumer diagnostic is returned.

- [x] **Step 4: Add consumer exception closes source test**

Make the consumer throw `IllegalStateException("decoder exploded")`. Assert startup returns unsupported diagnostic `GameStream RTP video consumer failed: decoder exploded`, closes the source once, and records one consumer start attempt.

- [x] **Step 5: Add stop closes source once test**

Start successfully, call stop twice, and assert the source close count is 1.

- [x] **Step 6: Run RED session client tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtpVideoSessionClientTest
```

Expected: fail because `GameStreamRtpPacketSourceFactory`, `GameStreamRtpVideoConsumer`, and `GameStreamRtpVideoSessionClient` do not exist.

Result: the focused Gradle run failed at `:app:compileDebugUnitTestJavaWithJavac` because `GameStreamRtpPacketSourceFactory`, `GameStreamRtpVideoConsumer`, and `GameStreamRtpVideoSessionClient` did not exist.

Additional RED result: after adding the consumer-exception cleanup regression during review, the focused test run failed because `IllegalStateException("decoder exploded")` escaped from `GameStreamRtpVideoSessionClient.start()`.

### Task 6: GREEN RTP Video Session Client

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpPacketSourceFactory.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoConsumer.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoSessionClient.java`

- [x] **Step 1: Add source factory and consumer interfaces**

Create `GameStreamRtpPacketSourceFactory.create(GameStreamEndpointPlan plan, GameStreamRtspSessionInfo sessionInfo)` and `GameStreamRtpVideoConsumer.start(GameStreamEndpointPlan plan, GameStreamRtspSessionInfo sessionInfo, EncodedVideoSampleProvider sampleProvider)`.

- [x] **Step 2: Add session client start path**

Create the source, wrap it in `GameStreamRtpVideoSampleProvider`, and pass the provider to the consumer. Return the consumer result. Convert source factory runtime failures into unsupported diagnostics prefixed with `GameStream RTP video source failed:`.

- [x] **Step 3: Add cleanup behavior**

Track a successfully started source. If the consumer rejects startup or throws during startup, close the source immediately. `stop()` closes the active source at most once and swallows close exceptions so upstream RTSP cleanup can continue.

- [x] **Step 4: Run GREEN session client tests**

Run the focused session client tests and confirm they pass.

Result: the focused `GameStreamRtpVideoSessionClientTest` run passed.

### Task 7: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-98-gamestream-rtp-video-packet-boundary.md`

- [x] **Step 1: Update docs**

Document that Android now has a fakeable RTP packet/sample boundary behind the GameStream video-session hook. State clearly that real UDP socket intake, GameStream-specific depacketization, and MediaCodec wiring for GameStream remain future work.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout/connect-timeout patterns.

Result: `git diff --check` passed, the working-tree diff scan returned `NO_MATCHES`, and the final staged `git diff --cached --check` plus staged diff scan also passed with `NO_MATCHES`.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk
adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity
```

Expected: Android tests/APK build, .NET solution tests, and emulator launch smoke pass.

Result: `gradle --no-daemon -p src\Beacon.Android test assembleDebug` passed, `dotnet test Beacon.slnx` passed with 296 total .NET tests, `adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk` returned `Success`, and `adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity` returned `Status: ok` with `LaunchState: COLD`.

- [ ] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
