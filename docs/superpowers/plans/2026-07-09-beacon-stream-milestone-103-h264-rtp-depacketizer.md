# Milestone 103: H.264 RTP Depacketizer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert H.264 RTP payloads from GameStream video packets into Annex B encoded samples before they reach Android's MediaCodec path.

**Architecture:** Add a small H.264-specific RTP payload reader behind the existing fakeable `RtpPacketSource` boundary. It supports the packetization forms needed first from RFC 6184: single NAL unit packets, STAP-A aggregation packets, and FU-A fragmented NAL units. `GameStreamRtpVideoSessionClient` selects this reader only when `GameStreamEndpointPlan.metadataValue("codec")` is `h264`; HEVC, AV1, dynamic client-port leasing, jitter buffering, packet reordering, retransmission, SPS/PPS extraction, and codec config injection remain future milestones.

**Tech Stack:** Java Android stream boundaries, JVM unit tests with fake RTP packet sources, IETF RFC 6184 as the H.264 RTP payload reference, README/extraction-map documentation, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED H.264 RTP Payload Reader Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/H264RtpSampleProviderTest.java`

- [x] **Step 1: Add single NAL unit test**

Create an RTP packet with payload `{0x65, 0x11, 0x22}` and timestamp `90000`. Assert `H264RtpSampleProvider.nextSample()` returns Annex B bytes `{0,0,0,1,0x65,0x11,0x22}` with presentation timestamp `0`.

- [x] **Step 2: Add STAP-A aggregation test**

Create a STAP-A payload with NAL type `24`, then two 16-bit big-endian NAL sizes and NAL payloads `{0x67,0x01}` and `{0x68,0x02}`. Assert the returned sample contains both NAL units, each with a 4-byte Annex B start code, using the packet timestamp.

- [x] **Step 3: Add FU-A fragmentation test**

Create two RTP packets with the same timestamp: a FU-A start payload reconstructing NAL header `0x65` with fragment bytes `{0x11,0x22}`, then a FU-A end payload with bytes `{0x33}`. Assert the provider buffers the first fragment, returns one sample on the end fragment, and emits `{0,0,0,1,0x65,0x11,0x22,0x33}`.

- [x] **Step 4: Add malformed payload tests**

Assert unsupported H.264 packetization types, truncated STAP-A sizes, FU-A end without start, and interleaved FU-A timestamps throw `IllegalStateException` with messages prefixed by `H.264 RTP payload failed:`.

- [x] **Step 5: Run RED H.264 tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.H264RtpSampleProviderTest
```

Expected: fail because `H264RtpSampleProvider` does not exist.

### Task 2: GREEN H.264 RTP Payload Reader

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/H264RtpSampleProvider.java`

- [x] **Step 1: Add Annex B start-code helper**

Implement a private `writeStartCode(ByteArrayOutputStream output)` helper that writes `{0,0,0,1}` before every reconstructed NAL unit.

- [x] **Step 2: Support single NAL unit packets**

For H.264 NAL types `1..23`, return one Annex B sample containing the original payload after a start code.

- [x] **Step 3: Support STAP-A packets**

For type `24`, parse repeated 16-bit big-endian lengths and NAL payloads, append a start code before each NAL, and return one sample containing all aggregated NAL units. Reject truncated length fields, zero-length NALs, and truncated NAL payloads.

- [x] **Step 4: Support FU-A packets**

For type `28`, parse the FU indicator/header, reconstruct the NAL header from FU indicator `F/NRI` plus FU header type on the start fragment, buffer fragment payload bytes until the end fragment, and return one sample only when the end fragment arrives. Reject end/continuation without start, start+end in one FU-A, reserved-bit headers, timestamp changes during a fragment, and payloads shorter than two bytes.

- [x] **Step 5: Preserve RTP timestamp behavior**

Use the same 90 kHz timestamp conversion and wraparound behavior as `GameStreamRtpVideoSampleProvider`.

- [x] **Step 6: Run GREEN H.264 tests**

Run the focused `H264RtpSampleProviderTest` command again and confirm it passes.

### Task 3: RED GameStream H.264 Selection Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtpVideoSessionClientTest.java`

- [x] **Step 1: Add H.264 provider selection test**

Start `GameStreamRtpVideoSessionClient` with a plan whose metadata includes `codec=h264`, `container=annex-b`, `width=2560`, `height=1600`, and `fps=120`. Assert the consumer receives an `H264RtpSampleProvider`.

- [x] **Step 2: Add non-H.264 fallback test**

Start with a plan whose metadata includes `codec=hevc` and assert the consumer still receives the existing `GameStreamRtpVideoSampleProvider` until HEVC depacketization is implemented.

- [x] **Step 3: Run RED selection tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtpVideoSessionClientTest
```

Expected: fail because `GameStreamRtpVideoSessionClient` always creates `GameStreamRtpVideoSampleProvider`.

### Task 4: GREEN GameStream H.264 Selection

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoSessionClient.java`

- [x] **Step 1: Add codec-based sample provider factory method**

Create a private method that returns `new H264RtpSampleProvider(source)` when `plan.metadataValue("codec")` equals `h264` ignoring case, otherwise returns `new GameStreamRtpVideoSampleProvider(source)`.

- [x] **Step 2: Use the selected provider in consumer startup**

Replace the direct `new GameStreamRtpVideoSampleProvider(source)` call with the selected provider.

- [x] **Step 3: Run GREEN selection tests**

Run the focused `GameStreamRtpVideoSessionClientTest` command again and confirm it passes.

### Task 5: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-103-h264-rtp-depacketizer.md`

- [x] **Step 1: Update docs**

Document that Milestone 103 adds H.264 single NAL, STAP-A, and FU-A depacketization to Annex B samples, while HEVC, AV1, jitter/reordering, dynamic port leasing, and parameter-set injection remain future work.

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
