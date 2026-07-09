# Milestone 92: RTSP SETUP Negotiation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the fakeable Android GameStream RTSP state machine from OPTIONS/DESCRIBE into SETUP negotiation, capturing the RTSP session id and server RTP ports from response headers without adding a default socket transport.

**Architecture:** Keep the network boundary injected through `RtspTransport`. This milestone only adds request construction, response parsing, and deterministic sequencing for audio/video/control setup. It must not add TCP sockets, retries, sleeps, or timeout cancellation. The default `GameStreamNativeStreamClient` still reports RTSP transport not configured until a lifecycle-owned transport is added.

**Source Reference:** Moonlight common-c `RtspConnection.c` sends RTSP `SETUP` with `Transport: unicast;X-GS-ClientPort=50000-50001`, `If-Modified-Since: Thu, 01 Jan 1970 00:00:00 GMT`, includes the `Session` header after it is known, and parses `server_port=` from the response `Transport` header. Beacon implements original Java boundary code and does not copy source.

**Tech Stack:** Java Android RTSP request/response helpers, fakeable RTSP transport, JVM unit tests, README/extraction-map documentation.

---

### Task 1: RED RTSP Request SETUP Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtspRequestTest.java`

- [x] **Step 1: Add SETUP request without session test**

Assert `RtspRequest.setup("streamid=audio/0/0", 3, "127.0.0.1:48010", "")` serializes:
- request line `SETUP streamid=audio/0/0 RTSP/1.0`.
- `CSeq: 3`.
- `Transport: unicast;X-GS-ClientPort=50000-50001`.
- `If-Modified-Since: Thu, 01 Jan 1970 00:00:00 GMT`.
- no `Session` header.

- [x] **Step 2: Add SETUP request with session test**

Assert `RtspRequest.setup(..., "abc123")` includes `Session: abc123`.

- [x] **Step 3: Run RED request tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.RtspRequestTest
```

Expected: fail because `RtspRequest.setup()` does not exist.

Observed RED:
- The focused Gradle command failed at `:app:compileDebugUnitTestJavaWithJavac` because `RtspRequest.setup()` did not exist.

### Task 2: RED RTSP SETUP Response Parsing Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspSetupResultTest.java`

- [x] **Step 1: Add session id parser test**

Parse `Session: abc123;timeout=30` and assert session id is `abc123`.

- [x] **Step 2: Add server port parser test**

Parse `Transport: unicast;server_port=48000-48001;source=127.0.0.1` and assert server port is `48000`.

- [x] **Step 3: Add malformed transport diagnostic test**

Assert missing or invalid `server_port=` produces a clear diagnostic.

- [x] **Step 4: Run RED parser tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtspSetupResultTest
```

Expected: fail because the setup result parser does not exist.

Observed RED:
- The focused Gradle command failed at `:app:compileDebugUnitTestJavaWithJavac` because `GameStreamRtspSetupResult` did not exist.

### Task 3: RED Handshake SETUP Sequencing Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspHandshakeClientTest.java`

- [x] **Step 1: Add full setup sequence success test**

Use a fake transport with five `200 OK` responses: OPTIONS, DESCRIBE, audio SETUP, video SETUP, control SETUP. Assert:
- five serialized requests were sent.
- audio SETUP is request 3 and has no `Session`.
- video SETUP is request 4 and includes `Session: session-1`.
- control SETUP is request 5 and includes `Session: session-1`.
- result status includes audio/video/control server ports.

- [x] **Step 2: Add setup failure test**

Return `503 Busy` for video SETUP and assert the result fails with `RTSP SETUP video failed with status 503 Busy.`

- [x] **Step 3: Add malformed setup transport test**

Return `200 OK` for audio SETUP without a valid `Transport: server_port=` and assert the result fails with the parser diagnostic.

- [x] **Step 4: Run RED handshake tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtspHandshakeClientTest
```

Expected: fail because the handshake client stops after DESCRIBE and no setup parser exists.

Observed RED:
- The focused Gradle command failed at `:app:compileDebugUnitTestJavaWithJavac` for missing setup request/parser code before reaching the handshake sequencing assertions.

### Task 4: GREEN SETUP Negotiation

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspRequest.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSetupResult.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspHandshakeClient.java`

- [x] **Step 1: Add SETUP request construction**

Implement `RtspRequest.setup(String target, int cseq, String host, String sessionId)` with stable header order.

- [x] **Step 2: Add setup response parser**

Implement a small immutable `GameStreamRtspSetupResult` with:
- `fromResponse(String streamName, RtspResponse response)`.
- `sessionId()`.
- `serverPort()`.
- `diagnostic()`.
- success flag.

The parser should trim `Session` before `;` and validate `server_port` as `1..65535`.

- [x] **Step 3: Extend handshake sequencing**

After DESCRIBE succeeds, send:
- audio SETUP target `streamid=audio/0/0`.
- video SETUP target `streamid=video/0/0`.
- control SETUP target `streamid=control/13/0`.

Capture session id from the first setup response and include it in later setup requests. Return diagnostics for non-2xx setup responses or parser failures.

- [x] **Step 4: Run GREEN focused tests**

Run the focused tests from Tasks 1-3 and confirm they pass.

Observed GREEN:
- The focused Gradle command passed for `RtspRequestTest`, `GameStreamRtspSetupResultTest`, and `GameStreamRtspHandshakeClientTest`.
- `GameStreamNativeStreamClientTest.startsRealHandshakeClientForCompleteGameStreamEndpointMap` then failed because its fake transport still returned bare 200 responses for SETUP. Root cause: the new setup parser correctly requires `Transport: server_port=...`. After updating the fake transport to return setup headers, the focused native-client test passed.

### Task 5: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-92-rtsp-setup-negotiation.md`

- [x] **Step 1: Update docs**

Document that Android now has fakeable RTSP OPTIONS/DESCRIBE/SETUP sequencing, session id capture, and server RTP port parsing, while default APK wiring still avoids socket transport.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout patterns.

Observed:
- `git diff --check` passed before staging tracked modifications.
- `git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("` found no matches before staging.
- `git diff --cached --check` passed after staging all milestone files.
- `git diff --cached -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("` found no matches after staging.

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
