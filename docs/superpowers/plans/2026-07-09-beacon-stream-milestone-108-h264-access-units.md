# Milestone 108: H.264 RTP Access Unit Assembly Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Android H.264 GameStream RTP decoding emit one Annex B sample per RTP access unit instead of one sample per packet/NAL when multiple packets share a frame timestamp.

**Architecture:** Keep `RtpReorderingPacketSource` as the packet ordering boundary and keep `H264RtpSampleProvider` as the H.264 depacketization boundary. Inside `H264RtpSampleProvider`, convert each RTP packet payload into Annex B bytes, append bytes with the same RTP timestamp into a pending access-unit buffer, return the buffer when the RTP marker bit indicates the last packet of the access unit, and flush a pending access unit when a new timestamp arrives or the source ends. Do not introduce sleeps, timeouts, retransmission, jitter timing, or interleaved packetization mode.

**Tech Stack:** Java 17, Android minSdk 26, JVM unit tests, RFC 6184 marker/timestamp behavior, existing RTP packet/sample-provider boundary, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED H.264 Access Unit Assembly Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/H264RtpSampleProviderTest.java`
- Modify later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/H264RtpSampleProvider.java`

- [x] **Step 1: Add marker-bit packet helper**

Add a test helper overload:

```java
private static RtpPacket packet(int sequenceNumber, long timestamp, boolean marker, byte[] payload) {
    byte[] bytes = new byte[12 + payload.length];
    bytes[0] = (byte) 0x80;
    bytes[1] = (byte) (0x60 | (marker ? 0x80 : 0));
    bytes[2] = (byte) ((sequenceNumber >>> 8) & 0xFF);
    bytes[3] = (byte) (sequenceNumber & 0xFF);
    bytes[4] = (byte) ((timestamp >>> 24) & 0xFF);
    bytes[5] = (byte) ((timestamp >>> 16) & 0xFF);
    bytes[6] = (byte) ((timestamp >>> 8) & 0xFF);
    bytes[7] = (byte) (timestamp & 0xFF);
    bytes[11] = 0x01;
    System.arraycopy(payload, 0, bytes, 12, payload.length);
    return RtpPacket.parse(bytes);
}
```

Update the existing helper `packet(int sequenceNumber, long timestamp, byte[] payload)` to delegate with `marker=true` for existing one-packet sample tests. For the existing FU-A two-packet helper calls, pass `marker=false` on the start fragment and `marker=true` on the end fragment.

- [x] **Step 2: Add single-NAL access-unit grouping test**

Add `groupsSameTimestampSingleNalPacketsUntilMarker()`:
- source packets:
  - packet 1 timestamp `90000`, marker `false`, payload `{0x41, 0x11}`;
  - packet 2 timestamp `90000`, marker `true`, payload `{0x41, 0x22}`.
- assert the first sample data is `{start, 0x41, 0x11, start, 0x41, 0x22}`;
- assert presentation time is `0`.

- [x] **Step 3: Add timestamp-change flush test**

Add `flushesPendingAccessUnitWhenTimestampChangesBeforeMarker()`:
- source packets:
  - packet 1 timestamp `90000`, marker `false`, payload `{0x41, 0x11}`;
  - packet 2 timestamp `91500`, marker `true`, payload `{0x41, 0x22}`.
- assert first sample data is `{start, 0x41, 0x11}`;
- assert second sample data is `{start, 0x41, 0x22}`;
- assert second presentation time is `16666`.

- [x] **Step 4: Add source-end flush test**

Add `flushesPendingAccessUnitAtEndOfSource()`:
- source packet: packet 1 timestamp `90000`, marker `false`, payload `{0x41, 0x11}`;
- assert first sample data is `{start, 0x41, 0x11}`;
- assert the next sample is EOS.

- [x] **Step 5: Add FU-A grouping with adjacent NAL test**

Add `groupsFuAAndSingleNalWithSameTimestampUntilMarker()`:
- source packets:
  - FU-A start timestamp `90000`, marker `false`, payload `{0x7C, 0x85, 0x11}`;
  - FU-A end timestamp `90000`, marker `false`, payload `{0x7C, 0x45, 0x22}`;
  - single NAL timestamp `90000`, marker `true`, payload `{0x41, 0x33}`.
- assert one sample containing `{start, 0x65, 0x11, 0x22, start, 0x41, 0x33}`.

- [x] **Step 6: Run RED access-unit tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.H264RtpSampleProviderTest
```

Expected: fail because `H264RtpSampleProvider` returns each completed RTP payload as its own sample instead of accumulating by timestamp/marker.

### Task 2: GREEN H.264 Access Unit Assembly

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/H264RtpSampleProvider.java`

- [x] **Step 1: Add pending access-unit state**

Add:
- `ByteArrayOutputStream activeAccessUnit`;
- `long activeAccessUnitTimestamp`;
- `RtpPacket pendingPacket`.

Keep `activeFragment` for FU-A packet assembly.

- [x] **Step 2: Split packet payload decoding from sample creation**

Change `sampleFromPayload(...)` so it returns decoded Annex B bytes rather than an `EncodedVideoSample`. Single NAL and STAP-A return bytes immediately. FU-A returns bytes only when the end fragment arrives, returns null for intermediate fragments, and keeps the existing fragment validation diagnostics.

- [x] **Step 3: Add access-unit append/flush logic**

In `nextSample()`:
- read `pendingPacket` first when present, otherwise `source.nextPacket()`;
- if the source ends and an FU-A fragment is active, keep the existing `FU-A stream ended before end fragment.` failure;
- if the source ends and an access unit buffer is active, flush it as a sample;
- when a decoded NAL byte array arrives:
  - if no access unit is active, start one with that packet timestamp;
  - if the packet timestamp differs from the active access-unit timestamp, store the current packet in `pendingPacket` and flush the active access unit;
  - otherwise append decoded bytes to the active access unit;
  - if `packet.marker()` is true, flush the active access unit;
  - otherwise continue reading.

Use the existing timestamp-to-presentation conversion in `sample(...)`; do not add any timer or wait.

- [x] **Step 4: Preserve parameter-set injection at access-unit flush**

Call `sample(activeAccessUnitTimestamp, activeAccessUnit.toByteArray())` only when flushing an access unit, so configured SPS/PPS are prepended once before the first VCL-containing access unit.

- [x] **Step 5: Run GREEN access-unit tests**

Run the focused `H264RtpSampleProviderTest` command again and confirm it passes.

### Task 3: RED RTP Session Integration Test

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtpVideoSessionClientTest.java`
- Modify later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoSessionClient.java`

- [x] **Step 1: Add session-level access-unit grouping test**

Add `h264RtpProviderGroupsSameTimestampPacketsInSession()`:
- source packets:
  - packet 1 timestamp `90000`, marker `false`, payload `{0x41, 0x11}`;
  - packet 2 timestamp `90000`, marker `true`, payload `{0x41, 0x22}`.
- start with `codec=h264`;
- assert the consumer provider emits one sample with both Annex B NAL units.

Use a local marker-bit packet helper overload matching the provider test helper.

- [x] **Step 2: Run RED/GREEN session test**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtpVideoSessionClientTest
```

Expected after Task 2: pass without additional production changes, proving the behavior reaches the session boundary through the existing provider wiring.

### Task 4: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-108-h264-access-units.md`

- [x] **Step 1: Update docs**

Document that Milestone 108 groups H.264 RTP packets with the same timestamp into one decoder sample/access unit and flushes on marker bit, timestamp change, or source end. State that this does not add jitter timing, retransmission, HEVC/AV1 depacketization, audio/control RTP processing, controller, or native touch/gesture protocol support.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
$matches = git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("; if ($LASTEXITCODE -eq 1) { "NO_MATCHES"; exit 0 }; if ($LASTEXITCODE -eq 0) { $matches; exit 1 }; exit $LASTEXITCODE
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout patterns.

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
