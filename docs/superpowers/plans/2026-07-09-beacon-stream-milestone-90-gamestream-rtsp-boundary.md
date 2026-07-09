# Milestone 90: GameStream RTSP Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Android GameStream/Moonlight "complete endpoint map but native decode is not implemented" dead end with a real, fakeable RTSP session boundary that can be validated without a phone.

**Architecture:** Keep RTP video, audio, control, input, and full Moonlight/GameStream decode out of this milestone. The APK should parse the server-owned endpoint map, validate the RTSP endpoint with Java URI parsing, build deterministic RTSP request/response messages, and call a `GameStreamRtspSessionClient` interface from `GameStreamNativeStreamClient`. Production Android must not add blocking socket behavior to the current synchronous `start()` path in this milestone; a blocking TCP client needs an explicit session lifecycle and cancellation policy first. Tests should drive the boundary with fake RTSP clients.

**Tech Stack:** Java Android stream descriptor parsing, JVM unit tests, fakeable Android native stream session interfaces, README/spec-plan documentation.

---

### Task 1: RED RTSP Endpoint Plan Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamEndpointPlanTest.java`

- [x] **Step 1: Add RTSP endpoint accessor test**

Add a test proving a complete GameStream endpoint map exposes:
- normalized `rtspUri()`.
- `rtspHost()`.
- `rtspPort()`.
- `rtspPath()`.

Use `rtsp://127.0.0.1:48010/beacon/session`.

- [x] **Step 2: Add unsupported RTSP scheme test**

Add a test proving `rtspReady()` is false and `rtspDiagnostic()` is explicit when the required RTSP role is present but its URI is not `rtsp://`.

- [x] **Step 3: Run RED endpoint tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamEndpointPlanTest
```

Expected: fail because the RTSP URI accessors and diagnostics do not exist.

Observed RED:
- The focused Gradle command failed at `:app:compileDebugUnitTestJavaWithJavac` because `GameStreamEndpointPlan.rtspReady()`, `rtspUri()`, `rtspHost()`, `rtspPort()`, `rtspPath()`, and `rtspDiagnostic()` did not exist.

### Task 2: RED RTSP Message Codec Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtspRequestTest.java`
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtspResponseTest.java`

- [x] **Step 1: Add OPTIONS request serialization test**

Assert an OPTIONS request serializes a request line, `CSeq`, `Host`, `X-GS-ClientVersion`, and a final blank line.

- [x] **Step 2: Add DESCRIBE request serialization test**

Assert a DESCRIBE request includes `Accept: application/sdp` and `If-Modified-Since: Thu, 01 Jan 1970 00:00:00 GMT`.

- [x] **Step 3: Add response parser tests**

Assert parsing `RTSP/1.0 200 OK` with headers exposes status code, reason phrase, and headers case-insensitively. Add a malformed status-line test with a clear diagnostic.

- [x] **Step 4: Run RED codec tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.RtspRequestTest --tests dev.beacon.android.RtspResponseTest
```

Expected: fail because the RTSP message classes do not exist.

Observed RED:
- The focused Gradle command failed at `:app:compileDebugUnitTestJavaWithJavac` because `RtspRequest` and `RtspResponse` did not exist.

### Task 3: RED Native Client Session Boundary Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamNativeStreamClientTest.java`

- [x] **Step 1: Add fake RTSP session success test**

Construct `GameStreamNativeStreamClient` with a fake `GameStreamRtspSessionClient`. For a complete endpoint map with RTSP ready, assert:
- the fake receives the `GameStreamEndpointPlan`.
- `start()` returns success.
- the diagnostic/status includes protocol and RTSP URI.

- [x] **Step 2: Add fake RTSP session failure test**

When the fake returns failure, assert `GameStreamNativeStreamClient` returns unsupported with the fake diagnostic.

- [x] **Step 3: Preserve incomplete endpoint test**

Keep the existing incomplete endpoint diagnostic unchanged.

- [x] **Step 4: Run RED client tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamNativeStreamClientTest
```

Expected: fail because the RTSP session interface and constructor do not exist.

Observed RED:
- The focused Gradle command failed at `:app:compileDebugUnitTestJavaWithJavac` because `GameStreamRtspSessionClient`, `GameStreamRtspSessionResult`, and the `GameStreamNativeStreamClient(GameStreamRtspSessionClient)` constructor did not exist.

### Task 4: GREEN RTSP Boundary Implementation

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamEndpointPlan.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspRequest.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspResponse.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSessionClient.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSessionResult.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamNativeStreamClient.java`

- [x] **Step 1: Add RTSP endpoint plan accessors**

Use `java.net.URI` for bounded parsing. Only `rtsp://` is RTSP-ready in this milestone. Keep unsupported/missing diagnostics explicit.

- [x] **Step 2: Add deterministic RTSP message codec**

Implement request serialization and response parsing for the first session boundary. Keep the codec small and protocol-shaped enough for OPTIONS/DESCRIBE/SETUP/PLAY work in later milestones.

- [x] **Step 3: Add fakeable RTSP session interface**

Add `GameStreamRtspSessionClient.start(GameStreamEndpointPlan plan)` and `GameStreamRtspSessionResult`. The default client should be explicit and non-blocking for now: it reports that native RTSP transport is not configured until the lifecycle layer can own blocking network work.

- [x] **Step 4: Route complete GameStream maps through the session boundary**

`GameStreamNativeStreamClient.start()` should:
- still reject incomplete maps.
- reject complete maps with invalid RTSP endpoints.
- call the configured RTSP session client for complete, RTSP-ready maps.
- return a successful `NativeStreamStartResult` when the session client succeeds.

- [x] **Step 5: Run GREEN focused Android tests**

Run the focused tests from Tasks 1-3 and confirm they pass.

Observed GREEN:
- The focused Gradle command passed for `GameStreamEndpointPlanTest`, `GameStreamNativeStreamClientTest`, `RtspRequestTest`, and `RtspResponseTest`.

### Task 5: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-90-gamestream-rtsp-boundary.md`

- [x] **Step 1: Update README**

Document that Android GameStream/Moonlight handling now has a fakeable RTSP session boundary and message codec, but the default APK still does not open blocking native RTSP sockets until a lifecycle/cancellation policy is implemented.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)"
```

Expected: no whitespace errors and no new timeout/cancellation or socket-timeout patterns.

Observed:
- `git diff --check` passed before staging tracked modifications.
- `git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)"` found no matches before staging.
- `git diff --cached --check` passed after staging all milestone files.
- `git diff --cached -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)"` found no matches after staging.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
```

Expected: Android tests/APK build and .NET solution tests pass.

Observed:
- Focused Android RED command failed at `:app:compileDebugUnitTestJavaWithJavac` for the missing RTSP plan/session/message classes and accessors.
- Focused Android GREEN command passed for `GameStreamEndpointPlanTest`, `GameStreamNativeStreamClientTest`, `RtspRequestTest`, and `RtspResponseTest`.
- The first full Android `test assembleDebug` run exposed one stale router-level expectation in `DiagnosticNativeStreamClientTest`; root cause was that the test still expected the old "native GameStream decode is not implemented yet" message after the protocol client contract changed.
- After updating that stale expectation, `gradle --no-daemon -p src\Beacon.Android test assembleDebug` passed.
- `dotnet test Beacon.slnx` passed.

- [ ] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
