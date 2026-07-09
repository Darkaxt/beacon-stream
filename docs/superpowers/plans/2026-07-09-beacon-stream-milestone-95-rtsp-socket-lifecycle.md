# Milestone 95: RTSP Socket Lifecycle Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a lifecycle-owned Android RTSP session layer that can open a socket-backed RTSP transport through an injectable connector, keep the transport alive after a successful handshake, and close it through the native stream `stop()` path. The default APK must remain not-configured and must not open sockets from the synchronous stream start path.

**Architecture:** Keep `GameStreamRtspSessionClient` injectable. Add a closeable transport lease factory and a socket connector boundary. The real Java socket connector is available for later lifecycle wiring, but the default `GameStreamNativeStreamClient()` continues to use `GameStreamRtspSessionClient.notConfigured()`. This milestone must not add connect timeouts, socket timeouts, sleeps, retry loops, or default socket wiring.

**Tech Stack:** Java Android RTSP session lifecycle helpers, fake connector/lease tests, JVM unit tests, README/extraction-map documentation.

---

### Task 1: RED Native Client Stop Lifecycle Test

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamNativeStreamClientTest.java`

- [x] **Step 1: Add stop delegation test**

Use a `RecordingRtspSessionClient` that returns success. Start a complete GameStream endpoint map, call `GameStreamNativeStreamClient.stop()`, and assert the RTSP session client's `stop()` was called exactly once.

- [x] **Step 2: Add failed start does not stop test**

Use a `RecordingRtspSessionClient` that returns failure. Start a complete GameStream endpoint map, call `stop()`, and assert the RTSP session client's `stop()` was not called.

- [x] **Step 3: Run RED native client tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamNativeStreamClientTest
```

Expected: fail because `GameStreamRtspSessionClient` has no stop hook and `GameStreamNativeStreamClient.stop()` is a no-op.

Result: the focused combined RED run failed at `:app:compileDebugUnitTestJavaWithJavac` because the stop hook, lease/session classes, and socket connector classes did not exist.

### Task 2: RED RTSP Transport Session Client Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspTransportSessionClientTest.java`

- [x] **Step 1: Add successful session lease retention test**

Use a fake `RtspTransportLeaseFactory` that returns a lease backed by a successful fake transport. Assert:
- factory receives the plan.
- start succeeds.
- lease remains open after start.
- `stop()` closes the lease once.

- [x] **Step 2: Add failed handshake closes lease test**

Use a fake transport that fails OPTIONS. Assert start fails and the lease closes immediately.

- [x] **Step 3: Add replacement session closes previous lease test**

Start twice successfully and assert the first lease is closed before the second session is retained.

- [x] **Step 4: Add factory failure diagnostic test**

Use a factory that throws `RtspTransportException("RTSP socket open failed: refused")` and assert the session result fails with that diagnostic.

- [x] **Step 5: Run RED session lifecycle tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtspTransportSessionClientTest
```

Expected: fail because `GameStreamRtspTransportSessionClient`, `RtspTransportLease`, and `RtspTransportLeaseFactory` do not exist.

Result: the focused combined RED run failed at compile time for the missing session/lease classes.

### Task 3: RED Socket Transport Lease Factory Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtspSocketTransportLeaseFactoryTest.java`

- [x] **Step 1: Add fake connector socket transport test**

Use a fake `RtspSocketConnector` that records host/port and returns fake input/output streams. Open a lease from a complete RTSP plan, transact one OPTIONS request, and assert:
- connector receives the plan host and port.
- the transport writes the serialized request.
- closing the lease closes the fake socket handle.

- [x] **Step 2: Add socket open failure diagnostic test**

Use a fake connector that throws `IOException("refused")` and assert `open(...)` throws `RtspTransportException("RTSP socket open failed: refused")`.

- [x] **Step 3: Run RED socket factory tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.RtspSocketTransportLeaseFactoryTest
```

Expected: fail because socket connector/factory classes do not exist.

Result: the focused combined RED run failed at compile time for the missing socket connector/factory classes.

### Task 4: GREEN Lifecycle And Socket Boundary

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSessionClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamNativeStreamClient.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspTransportLease.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspTransportLeaseFactory.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspTransportSessionClient.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspSocketConnector.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspSocketHandle.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/JavaRtspSocketConnector.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspSocketTransportLeaseFactory.java`

- [x] **Step 1: Add RTSP session stop hook**

Add a default no-op `stop()` to `GameStreamRtspSessionClient`. Make `GameStreamNativeStreamClient` call it only after a successful GameStream RTSP session start.

- [x] **Step 2: Add closeable RTSP lease abstractions**

Implement `RtspTransportLease` and `RtspTransportLeaseFactory` so lifecycle code can own and close transports without assuming the concrete transport type.

- [x] **Step 3: Add transport session client**

Implement `GameStreamRtspTransportSessionClient` that opens a lease, runs `GameStreamRtspHandshakeClient`, retains a successful lease until `stop()`, closes failed or replaced leases, and converts factory/close diagnostics into non-crashing behavior.

- [x] **Step 4: Add socket connector boundary**

Implement `RtspSocketConnector`, `RtspSocketHandle`, `JavaRtspSocketConnector`, and `RtspSocketTransportLeaseFactory`. The Java connector may open `new Socket(host, port)` but must not set connect timeouts, socket timeouts, sleeps, or retries. It must not be wired into the default APK path in this milestone.

- [x] **Step 5: Run GREEN focused tests**

Run the focused tests from Tasks 1-3 and confirm they pass.

Result: the focused Gradle run passed for `GameStreamNativeStreamClientTest`, `GameStreamRtspTransportSessionClientTest`, and `RtspSocketTransportLeaseFactoryTest`.

### Task 5: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-95-rtsp-socket-lifecycle.md`

- [x] **Step 1: Update docs**

Document that Android now has socket transport lifecycle boundaries and stop cleanup, while default APK socket use remains disabled.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout/connect-timeout patterns.

Result: `git diff --check` passed, and the diff scan reported no new sleep/timeout/socket-timeout/connect-timeout patterns.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
```

Expected: Android tests/APK build and .NET solution tests pass.

Result: `gradle --no-daemon -p src\Beacon.Android test assembleDebug` passed, and `dotnet test Beacon.slnx` passed.

Additional emulator smoke: installed `src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk` on `emulator-5554` and launched `dev.beacon.android/.BeaconActivity`; Android reported `Status: ok`.

- [x] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.

Result: PR #96 merged after Android, client-lab, and dotnet CI checks passed.

### Task 6: Post-Sync Refactor Pass

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspTransportSessionClient.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspTransportSessionClientTest.java`

- [x] **Step 1: Add close/factory hardening tests**

Add regression coverage proving unexpected lease factory exceptions become diagnostics and unexpected lease close exceptions do not crash `stop()`.

Result: the focused Gradle run failed with `IllegalStateException` for both new tests before the refactor.

- [x] **Step 2: Harden the session client cleanup boundary**

Make `GameStreamRtspTransportSessionClient` convert unexpected factory failures to a diagnostic and swallow runtime lease-close failures on cleanup paths.

Result: the focused Gradle run passed for `GameStreamRtspTransportSessionClientTest`.

- [x] **Step 3: Validate refactor pass**

Run static checks, full Android test/build, .NET solution tests, and emulator launch smoke again.

Result: `git diff --check` passed; the diff scan reported no new sleep/timeout/socket-timeout/connect-timeout patterns; `gradle --no-daemon -p src\Beacon.Android test assembleDebug` passed; `dotnet test Beacon.slnx` passed; emulator reinstall and launch of `dev.beacon.android/.BeaconActivity` reported `Status: ok`.

- [ ] **Step 4: Sync refactor pass**

Commit, push, open a PR, wait for CI, and merge if checks are green.
