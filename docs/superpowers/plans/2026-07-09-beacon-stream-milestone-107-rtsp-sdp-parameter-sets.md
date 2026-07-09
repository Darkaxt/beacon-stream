# Milestone 107: RTSP DESCRIBE SDP Parameter Sets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Android GameStream H.264 RTP decoding use `sprop-parameter-sets` advertised in the RTSP DESCRIBE SDP video section when descriptors do not already provide SPS/PPS metadata.

**Architecture:** Preserve RTSP response bodies, add a small SDP metadata parser that extracts H.264 video `sprop-parameter-sets`, carry the extracted value in `GameStreamRtspSessionInfo`, and make `GameStreamRtpVideoSessionClient` prefer descriptor metadata while falling back to RTSP session metadata. This remains phone-free and does not add HEVC/AV1 depacketization, retransmission, jitter timing, audio RTP, or live GameStream encoder integration.

**Tech Stack:** Java 17, Android minSdk 26, JVM unit tests, RTSP `Content-Length` body handling, SDP line parsing, existing GameStream RTSP/RTP boundaries, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED RTSP Response Body Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtspResponseTest.java`
- Modify later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspResponse.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtspByteStreamTransportTest.java`
- Modify later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspByteStreamTransport.java`

- [x] **Step 1: Add `RtspResponse` body tests**

Add tests proving:
- `RtspResponse.parse(...)` preserves the text after the first RTSP header delimiter as `body()`;
- responses without a body return an empty `body()`.

- [x] **Step 2: Add byte-stream body retention test**

Extend the existing `consumesContentLengthBodyBeforeNextResponse` coverage so the first response exposes `body() == "abcde"` while the second response still parses cleanly.

- [x] **Step 3: Run RED RTSP body tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.RtspResponseTest --tests dev.beacon.android.RtspByteStreamTransportTest
```

Expected: fail because `RtspResponse.body()` does not exist and `RtspByteStreamTransport` discards `Content-Length` bodies.

### Task 2: GREEN RTSP Response Body Retention

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspResponse.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspByteStreamTransport.java`

- [x] **Step 1: Preserve body in `RtspResponse`**

Add a `body` field, expose `String body()`, and update `parse(String rawResponse)` to split at the first `\r\n\r\n` or `\n\n` delimiter. Header parsing continues to use only the header section; body text is preserved exactly after the delimiter.

- [x] **Step 2: Read response body in `RtspByteStreamTransport`**

Replace `consumeBody(response)` with `readBody(response)` that reads exactly `Content-Length` bytes from the input stream, decodes them as UTF-8, and returns that text. Parse the headers first, then return `response.withBody(body)` or an equivalent immutable copy. Keep invalid `Content-Length` and premature EOF diagnostics unchanged.

- [x] **Step 3: Run GREEN RTSP body tests**

Run the focused `RtspResponseTest` and `RtspByteStreamTransportTest` command again and confirm it passes.

### Task 3: RED SDP Metadata Parser Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspSdpMetadataTest.java`
- Create later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSdpMetadata.java`

- [x] **Step 1: Add SDP extraction tests**

Create tests proving:
- H.264 `sprop-parameter-sets` is extracted from the `m=video` section when `a=rtpmap:<pt> H264/90000` and matching `a=fmtp:<pt>` are present;
- audio-section `sprop-parameter-sets` is ignored;
- parameter names are matched case-insensitively and tolerate whitespace around `=`;
- when no H.264 `rtpmap` exists, the parser may fall back to a video-section `a=fmtp` line containing `sprop-parameter-sets`;
- missing H.264 video parameter sets return an empty value.

- [x] **Step 2: Run RED SDP parser tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtspSdpMetadataTest
```

Expected: fail because `GameStreamRtspSdpMetadata` does not exist.

### Task 4: GREEN SDP Metadata Parser

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSdpMetadata.java`

- [x] **Step 1: Implement parser value object**

Create `final class GameStreamRtspSdpMetadata` with:
- `static GameStreamRtspSdpMetadata from(String sdp)`;
- `String h264SpropParameterSets()`.

Parse by SDP line, track whether the current media section is video, collect H.264 payload ids from `a=rtpmap:<pt> H264/90000`, and inspect video-section `a=fmtp:<pt> ...` attributes for `sprop-parameter-sets`. Prefer an `fmtp` whose payload id has an H.264 `rtpmap`; otherwise use the first video-section `fmtp` containing `sprop-parameter-sets`. Return the raw comma-separated value without Base64 validation, because `H264ParameterSets` owns validation at RTP consumer startup.

- [x] **Step 2: Run GREEN SDP parser tests**

Run the focused `GameStreamRtspSdpMetadataTest` command again and confirm it passes.

### Task 5: RED RTSP Handshake Session Metadata Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspHandshakeClientTest.java`
- Modify later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspHandshakeClient.java`
- Modify later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSessionInfo.java`

- [x] **Step 1: Add DESCRIBE SDP capture test**

Add a test where the DESCRIBE response has `Content-Type: application/sdp`, a `Content-Length`, and a body containing video H.264 `sprop-parameter-sets`. Assert the successful result's `sessionInfo().h264SpropParameterSets()` returns `Z0IAHg==,aM4G4g==`.

- [x] **Step 2: Add empty SDP metadata test**

Assert a successful handshake with no H.264 SDP parameter sets returns an empty `sessionInfo().h264SpropParameterSets()`.

- [x] **Step 3: Run RED handshake tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtspHandshakeClientTest
```

Expected: fail because session info does not expose RTSP SDP H.264 parameter-set metadata yet.

### Task 6: GREEN RTSP Handshake Session Metadata

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSessionInfo.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspHandshakeClient.java`

- [x] **Step 1: Extend session info**

Add an immutable `h264SpropParameterSets` field and getter. Existing `started(...)` and `startedWithClientPorts(...)` overloads pass an empty value. Add one overload that accepts the existing client/server port values, the optional `GameStreamRtpPortLease`, and `String h264SpropParameterSets`.

- [x] **Step 2: Parse DESCRIBE body in handshake**

After a successful DESCRIBE response, call `GameStreamRtspSdpMetadata.from(describe.body()).h264SpropParameterSets()` and pass that value into `GameStreamRtspSessionInfo.startedWithClientPorts(...)` after SETUP succeeds. Do not fail the RTSP handshake because the string is malformed Base64; defer that diagnostic to RTP startup.

- [x] **Step 3: Run GREEN handshake tests**

Run the focused `GameStreamRtspHandshakeClientTest` command again and confirm it passes.

### Task 7: RED RTP Session Fallback Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtpVideoSessionClientTest.java`
- Modify later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoSessionClient.java`

- [x] **Step 1: Add RTSP SDP fallback injection test**

Start `GameStreamRtpVideoSessionClient` with `codec=h264` but without descriptor parameter-set metadata. Pass session info containing `h264SpropParameterSets=Z0IAHg==,aM4G4g==`, feed one IDR RTP packet, and assert the emitted sample is SPS + PPS + IDR.

- [x] **Step 2: Add descriptor metadata precedence test**

Use descriptor metadata and session metadata with different SPS/PPS bytes. Assert descriptor metadata wins, so server-authored descriptor metadata can override what DESCRIBE advertised.

- [x] **Step 3: Run RED RTP fallback tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtpVideoSessionClientTest
```

Expected: fail because `GameStreamRtpVideoSessionClient` only reads descriptor metadata.

### Task 8: GREEN RTP Session Fallback

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoSessionClient.java`

- [x] **Step 1: Use session-info fallback**

Change H.264 provider creation to pass the first non-empty value from descriptor metadata aliases, then `sessionInfo.h264SpropParameterSets()`. Keep non-H.264 provider behavior unchanged.

- [x] **Step 2: Run GREEN RTP fallback tests**

Run the focused `GameStreamRtpVideoSessionClientTest` command again and confirm it passes.

### Task 9: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-107-rtsp-sdp-parameter-sets.md`

- [x] **Step 1: Update docs**

Document that Milestone 107 preserves RTSP DESCRIBE response bodies, extracts H.264 `sprop-parameter-sets` from the SDP video section, and uses that value as a fallback for RTP SPS/PPS injection. State that this still does not add HEVC/AV1 depacketization, jitter timing, retransmission, audio/control RTP processing, or controller/native touch protocols.

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
