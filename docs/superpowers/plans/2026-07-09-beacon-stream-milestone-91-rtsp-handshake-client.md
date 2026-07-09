# Milestone 91: RTSP Handshake Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deterministic Android GameStream RTSP handshake client that can run OPTIONS and DESCRIBE over an injected transport, so the APK has a real protocol state machine behind the Milestone 90 session boundary without opening blocking sockets in the default path.

**Architecture:** Keep the network transport fakeable. This milestone adds RTSP session sequencing and response validation only. The default `GameStreamNativeStreamClient` must still use the non-blocking `notConfigured()` client until Android owns an explicit lifecycle for real socket work. A future milestone can provide a TCP transport and run it from an owned stream lifecycle thread.

**Source Reference:** Moonlight common-c `RtspConnection.c` uses OPTIONS and DESCRIBE with `CSeq`, `Host`, `X-GS-ClientVersion`, `Accept: application/sdp`, and `If-Modified-Since` before later SETUP/PLAY stages. Beacon implements original Java code and does not copy source.

**Tech Stack:** Java Android stream descriptor parsing, fakeable RTSP transport, JVM unit tests, README/extraction-map documentation.

---

### Task 1: RED RTSP Handshake Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspHandshakeClientTest.java`

- [x] **Step 1: Add successful OPTIONS/DESCRIBE sequence test**

Create a fake `RtspTransport` that records serialized requests and returns `200 OK` for OPTIONS and DESCRIBE. Assert:
- `GameStreamRtspHandshakeClient.start(plan)` succeeds.
- request 1 is OPTIONS with `CSeq: 1`.
- request 2 is DESCRIBE with `CSeq: 2`.
- DESCRIBE includes `Accept: application/sdp`.
- status is `RTSP handshake completed. protocol=gamestream rtsp=...`.

- [x] **Step 2: Add OPTIONS failure test**

Return `RTSP/1.0 503 Busy` for OPTIONS. Assert the result fails with `RTSP OPTIONS failed with status 503 Busy.`

- [x] **Step 3: Add DESCRIBE failure test**

Return `200 OK` for OPTIONS and `404 Not Found` for DESCRIBE. Assert the result fails with `RTSP DESCRIBE failed with status 404 Not Found.`

- [x] **Step 4: Run RED tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtspHandshakeClientTest
```

Expected: fail because the handshake client and transport interface do not exist.

Observed RED:
- The focused Gradle command failed at `:app:compileDebugUnitTestJavaWithJavac` because `RtspTransport` and `GameStreamRtspHandshakeClient` did not exist.

### Task 2: GREEN RTSP Handshake Client

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspTransport.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspHandshakeClient.java`

- [x] **Step 1: Add RTSP transport interface**

Add `RtspTransport.transact(RtspRequest request)` returning `RtspResponse`. This is a synchronous fakeable boundary only; do not add sockets, retries, sleeps, or timeout cancellation.

- [x] **Step 2: Add handshake client**

Implement `GameStreamRtspHandshakeClient implements GameStreamRtspSessionClient`. It should:
- reject non-RTSP-ready plans with `plan.rtspDiagnostic()`.
- send OPTIONS with CSeq 1.
- stop on non-2xx OPTIONS.
- send DESCRIBE with CSeq 2.
- stop on non-2xx DESCRIBE.
- return a successful `GameStreamRtspSessionResult` with a clear status.

- [x] **Step 3: Run GREEN focused tests**

Run the focused test command from Task 1 and confirm it passes.

Observed GREEN:
- The focused Gradle command passed for `GameStreamRtspHandshakeClientTest` and `GameStreamNativeStreamClientTest`.

### Task 3: Integration Test Through Native Client

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamNativeStreamClientTest.java`

- [x] **Step 1: Add native client integration test with handshake client**

Configure `GameStreamNativeStreamClient` with `GameStreamRtspHandshakeClient` and a fake transport. Assert a complete endpoint map succeeds through the real handshake client, not a hand-written fake session client.

- [x] **Step 2: Run focused native client tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamNativeStreamClientTest --tests dev.beacon.android.GameStreamRtspHandshakeClientTest
```

Observed:
- The focused Gradle command passed for `GameStreamNativeStreamClientTest` and `GameStreamRtspHandshakeClientTest`.

### Task 4: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-91-rtsp-handshake-client.md`

- [x] **Step 1: Update docs**

Document that Android now has a fakeable RTSP OPTIONS/DESCRIBE handshake client, while default APK wiring still avoids socket transport until a lifecycle-owned TCP transport exists.

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
