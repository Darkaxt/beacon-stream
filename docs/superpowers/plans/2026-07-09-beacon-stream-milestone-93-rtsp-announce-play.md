# Milestone 93: RTSP ANNOUNCE And PLAY Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the fakeable Android GameStream RTSP state machine from SETUP negotiation into ANNOUNCE and PLAY sequencing, using an injected SDP payload boundary and still avoiding default blocking socket transport.

**Architecture:** Keep all network I/O behind `RtspTransport`. Keep SDP generation injectable so later milestones can replace the diagnostic payload with real GameStream session parameters. This milestone must not add TCP sockets, retries, sleeps, timeout cancellation, RTP video/audio/control, or default APK socket wiring.

**Source Reference:** Moonlight common-c `RtspConnection.c` sends ANNOUNCE with `Session`, `Content-type: application/sdp`, and `Content-length`, then sends PLAY with the active session. Beacon implements original Java boundary code and does not copy source.

**Tech Stack:** Java Android RTSP request/response helpers, fakeable RTSP transport, JVM unit tests, README/extraction-map documentation.

---

### Task 1: RED RTSP Request ANNOUNCE/PLAY Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtspRequestTest.java`

- [x] **Step 1: Add ANNOUNCE request test**

Assert `RtspRequest.announce("streamid=control/13/0", 6, "127.0.0.1:48010", "session-1", payload)` serializes:
- request line `ANNOUNCE streamid=control/13/0 RTSP/1.0`.
- `CSeq: 6`.
- `Session: session-1`.
- `Content-type: application/sdp`.
- byte-accurate `Content-length`.
- blank line followed by the SDP payload.

- [x] **Step 2: Add PLAY request test**

Assert `RtspRequest.play("/", 7, "127.0.0.1:48010", "session-1")` serializes:
- request line `PLAY / RTSP/1.0`.
- `CSeq: 7`.
- `Session: session-1`.
- no payload.

- [x] **Step 3: Run RED request tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.RtspRequestTest
```

Expected: fail because `RtspRequest.announce()` and `RtspRequest.play()` do not exist.

Observed RED:
- The focused Gradle command failed at `:app:compileDebugUnitTestJavaWithJavac` because `RtspRequest.announce()`, `RtspRequest.play()`, `GameStreamRtspSdpPayloadProvider`, and the injectable handshake constructor did not exist.

### Task 2: RED Handshake ANNOUNCE/PLAY Sequencing Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspHandshakeClientTest.java`

- [x] **Step 1: Add full ANNOUNCE/PLAY success test**

Use a fake transport with seven `200 OK` responses: OPTIONS, DESCRIBE, audio SETUP, video SETUP, control SETUP, ANNOUNCE, PLAY. Assert:
- seven serialized requests were sent.
- ANNOUNCE is request 6 and includes the injected SDP payload.
- PLAY is request 7 and targets `/`.
- result status includes session id and audio/video/control server ports.

- [x] **Step 2: Add empty SDP diagnostic test**

Inject an SDP provider that returns only whitespace and assert the result fails before ANNOUNCE with a clear diagnostic.

- [x] **Step 3: Add ANNOUNCE failure test**

Return `503 Busy` for ANNOUNCE and assert the result fails with `RTSP ANNOUNCE failed with status 503 Busy.`

- [x] **Step 4: Add PLAY failure test**

Return `404 Not Found` for PLAY and assert the result fails with `RTSP PLAY failed with status 404 Not Found.`

- [x] **Step 5: Run RED handshake tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtspHandshakeClientTest
```

Expected: fail because the handshake client stops after SETUP and no SDP provider exists.

Observed RED:
- The focused Gradle command failed at `:app:compileDebugUnitTestJavaWithJavac` for the missing SDP provider interface and injectable handshake constructor before reaching the old SETUP-only success assertions.

### Task 3: GREEN ANNOUNCE/PLAY Sequencing

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspRequest.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSdpPayloadProvider.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspHandshakeClient.java`

- [x] **Step 1: Add RTSP payload support**

Add an optional payload to `RtspRequest` and keep existing OPTIONS, DESCRIBE, and SETUP serialization unchanged.

- [x] **Step 2: Add ANNOUNCE and PLAY request construction**

Implement stable-header `announce(...)` and `play(...)` factories. ANNOUNCE must calculate content length using UTF-8 bytes.

- [x] **Step 3: Add injectable SDP payload boundary**

Add `GameStreamRtspSdpPayloadProvider` and a default diagnostic provider that produces a non-empty ASCII SDP payload. The default constructor should keep working by using the diagnostic provider.

- [x] **Step 4: Extend handshake sequencing**

After control SETUP succeeds:
- create SDP payload.
- reject empty payloads.
- send ANNOUNCE target `streamid=control/13/0` with session id.
- send PLAY target `/` with session id.
- return a `RTSP play started...` status on success.

- [x] **Step 5: Run GREEN focused tests**

Run the focused tests from Tasks 1-2 and confirm they pass.

Observed GREEN:
- The focused Gradle command passed for `RtspRequestTest` and `GameStreamRtspHandshakeClientTest`.
- `GameStreamNativeStreamClientTest` also passed against the updated real handshake client.

### Task 4: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-93-rtsp-announce-play.md`

- [x] **Step 1: Update docs**

Document that Android now has fakeable RTSP OPTIONS/DESCRIBE/SETUP/ANNOUNCE/PLAY sequencing, while default APK wiring still avoids socket transport.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout patterns.

Observed:
- `git diff --check` passed.
- `git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("` found no matches.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
```

Expected: Android tests/APK build and .NET solution tests pass.

Observed:
- `gradle --no-daemon -p src\Beacon.Android test assembleDebug` passed.
- `dotnet test Beacon.slnx` passed.

- [ ] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
