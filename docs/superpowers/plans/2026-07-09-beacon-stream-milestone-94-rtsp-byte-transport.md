# Milestone 94: RTSP Byte Transport Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a lifecycle-owned Android RTSP byte transport boundary that writes serialized RTSP requests to an owned stream, reads framed RTSP responses from that stream, closes deterministically, and reports I/O failures through handshake diagnostics. This is the next step toward a real TCP RTSP client without wiring the default APK to open blocking sockets yet.

**Architecture:** Keep `RtspTransport` injectable. Add a stream-backed transport that owns `InputStream`/`OutputStream` pairs supplied by a later socket lifecycle factory. This milestone must not add default APK socket wiring, background polling, sleeps, retry loops, or timeout-based cancellation. Java/Android `Socket` creation and lifecycle startup remain a later milestone so connection open can be moved off the UI path deliberately.

**Tech Stack:** Java Android RTSP transport helpers, fakeable byte streams, JVM unit tests, README/extraction-map documentation.

---

### Task 1: RED RTSP Byte Transport Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtspByteStreamTransportTest.java`

- [x] **Step 1: Add request write and response read test**

Given an input stream containing `RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n`, assert `RtspByteStreamTransport.transact(RtspRequest.options(...))`:
- writes the serialized request bytes to the output stream.
- returns a parsed `RtspResponse` with status `200` and `CSeq: 1`.

- [x] **Step 2: Add content-length framing test**

Given two concatenated responses where the first has `Content-Length: 5` and body `abcde`, assert two `transact(...)` calls parse the first and second response separately. This proves the transport consumes response bodies and keeps the stream aligned.

- [x] **Step 3: Add close ownership test**

Use close-tracking streams and assert `close()` closes both the input and output streams once.

- [x] **Step 4: Add EOF diagnostic test**

Given an empty input stream, assert `transact(...)` throws `RtspTransportException` with `RTSP response ended before a status line was read.`

- [x] **Step 5: Run RED transport tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.RtspByteStreamTransportTest
```

Expected: fail because `RtspByteStreamTransport` and `RtspTransportException` do not exist.

Observed RED:
- The focused Gradle command failed at `:app:compileDebugUnitTestJavaWithJavac` because `RtspByteStreamTransport` and `RtspTransportException` did not exist.

### Task 2: RED Handshake Transport Failure Diagnostic Test

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspHandshakeClientTest.java`

- [x] **Step 1: Add transport exception diagnostic test**

Use an `RtspTransport` that throws `RtspTransportException("RTSP write failed: disk full")` and assert the handshake result fails with that diagnostic rather than propagating an exception.

- [x] **Step 2: Run RED handshake test**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtspHandshakeClientTest
```

Expected: fail because `RtspTransportException` does not exist or is not caught by the handshake client.

Observed RED:
- The focused Gradle command failed at compile time for the missing `RtspTransportException` before it could reach the new handshake assertion.

### Task 3: GREEN Byte Transport And Diagnostics

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspByteStreamTransport.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspTransportException.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspHandshakeClient.java`

- [x] **Step 1: Add `RtspTransportException`**

Add an unchecked exception that carries user-visible transport diagnostics.

- [x] **Step 2: Add stream-backed RTSP transport**

Implement `RtspByteStreamTransport implements RtspTransport, AutoCloseable`:
- write request bytes as UTF-8.
- flush output after each request.
- read status/header bytes until `\r\n\r\n`.
- honor positive `Content-Length` by consuming exactly that many response body bytes.
- parse headers through existing `RtspResponse.parse(...)`.
- wrap I/O failures in `RtspTransportException`.
- close input and output streams deterministically.

- [x] **Step 3: Catch transport failures in handshake**

Wrap the RTSP handshake sequence in `try/catch (RtspTransportException ex)` and return `GameStreamRtspSessionResult.failed(ex.getMessage())`.

- [x] **Step 4: Run GREEN focused tests**

Run the focused tests from Tasks 1-2 and confirm they pass.

Observed GREEN:
- The focused Gradle command passed for `RtspByteStreamTransportTest` and `GameStreamRtspHandshakeClientTest`.

### Task 4: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-94-rtsp-byte-transport.md`

- [x] **Step 1: Update docs**

Document that Android now has a tested RTSP byte-stream transport boundary and transport diagnostics, while real socket creation/default APK wiring remain future work.

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
