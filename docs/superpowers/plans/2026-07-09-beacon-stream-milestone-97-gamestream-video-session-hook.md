# Milestone 97: GameStream Video Session Hook Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a structured Android handoff from successful GameStream RTSP setup into an optional video-session client so the next RTP media milestone can start from typed session data instead of parsing status strings.

**Architecture:** Keep the current RTSP-only behavior as the default so endpoint-only GameStream descriptors continue to work when no media client is configured. Add `GameStreamRtspSessionInfo` to carry protocol, RTSP URI, session id, and negotiated audio/video/control server ports. Add a fakeable `GameStreamVideoSessionClient` hook that `GameStreamNativeStreamClient` invokes only after RTSP succeeds and structured info is present; if media startup fails, the client releases the RTSP session immediately.

**Tech Stack:** Java Android client boundaries, JVM unit tests, README/extraction-map documentation, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED Structured RTSP Session Info Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspHandshakeClientTest.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspTransportSessionClientTest.java`

- [x] **Step 1: Add handshake session-info assertions**

In the successful handshake tests, assert:

```java
assertTrue(result.sessionInfo().present());
assertEquals("gamestream", result.sessionInfo().protocol());
assertEquals("rtsp://127.0.0.1:48010/beacon/session", result.sessionInfo().rtspUri());
assertEquals("session-1", result.sessionInfo().sessionId());
assertEquals(48000, result.sessionInfo().audioServerPort());
assertEquals(47998, result.sessionInfo().videoServerPort());
assertEquals(47999, result.sessionInfo().controlServerPort());
```

- [x] **Step 2: Add failed/default result info assertions**

Assert `GameStreamRtspSessionResult.failed("x").sessionInfo().present()` is false, and assert legacy `GameStreamRtspSessionResult.started("x")` also returns non-present info.

- [x] **Step 3: Run RED RTSP tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtspHandshakeClientTest --tests dev.beacon.android.GameStreamRtspTransportSessionClientTest
```

Expected: fail because `GameStreamRtspSessionResult.sessionInfo()` and `GameStreamRtspSessionInfo` do not exist.

Result: the focused Gradle run failed at `:app:compileDebugUnitTestJavaWithJavac` because `GameStreamRtspSessionResult.sessionInfo()` did not exist.

### Task 2: GREEN Structured RTSP Session Info

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSessionInfo.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSessionResult.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspHandshakeClient.java`

- [x] **Step 1: Add `GameStreamRtspSessionInfo`**

Create an immutable value object with `empty()`, `started(protocol, rtspUri, sessionId, audioServerPort, videoServerPort, controlServerPort)`, `present()`, and field getters. Treat blank strings or non-positive ports as non-present.

- [x] **Step 2: Extend `GameStreamRtspSessionResult`**

Keep `started(String status)` for old tests and callers, but add `started(String status, GameStreamRtspSessionInfo sessionInfo)`. Failed results always carry `GameStreamRtspSessionInfo.empty()`.

- [x] **Step 3: Return structured info from RTSP handshake**

When OPTIONS, DESCRIBE, SETUP, ANNOUNCE, and PLAY succeed, return `GameStreamRtspSessionResult.started(status, GameStreamRtspSessionInfo.started(...))` using the captured session id and ports.

- [x] **Step 4: Run GREEN RTSP tests**

Run the focused RTSP tests from Task 1 and confirm they pass.

Result: the focused `GameStreamRtspHandshakeClientTest` and `GameStreamRtspTransportSessionClientTest` run passed.

### Task 3: RED GameStream Video Hook Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamNativeStreamClientTest.java`

- [x] **Step 1: Add video hook starts after RTSP success test**

Construct `GameStreamNativeStreamClient` with a recording RTSP client returning a started result with session info and a recording video client returning a successful `NativeStreamStartResult`. Assert the video client receives the endpoint plan and session info, and the final native stream result is the video client's result.

- [x] **Step 2: Add video hook failure releases RTSP test**

Use the same setup but return `NativeStreamStartResult.unsupported("RTP video failed")` from the video client. Assert the final result fails with that diagnostic and the RTSP client `stop()` count is 1.

- [x] **Step 3: Add stop delegates video before RTSP test**

Start successfully with a video client, call `stop()`, and assert the video stop count is 1 and RTSP stop count is 1.

- [x] **Step 4: Run RED native client tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamNativeStreamClientTest
```

Expected: fail because `GameStreamVideoSessionClient` and the two-argument `GameStreamNativeStreamClient` constructor do not exist.

Result: the focused Gradle run failed at `:app:compileDebugUnitTestJavaWithJavac` because `GameStreamVideoSessionClient` and the two-argument `GameStreamNativeStreamClient` constructor did not exist.

### Task 4: GREEN GameStream Video Hook

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamVideoSessionClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamNativeStreamClient.java`

- [x] **Step 1: Add `GameStreamVideoSessionClient`**

Create a fakeable interface:

```java
public interface GameStreamVideoSessionClient {
    NativeStreamStartResult start(GameStreamEndpointPlan plan, GameStreamRtspSessionInfo sessionInfo);

    void stop();
}
```

- [x] **Step 2: Keep RTSP-only default behavior**

Keep the existing one-argument and no-argument constructors using no video client. When no video client is configured, a successful RTSP start returns the existing RTSP-only success status.

- [x] **Step 3: Invoke video hook when configured**

When RTSP succeeds and a video client is configured, require present session info. Start the video client with the plan and info. On video failure, stop the RTSP session and return the video diagnostic. On video success, track both RTSP and video as active and return the video result.

- [x] **Step 4: Stop owned media and RTSP state**

`stop()` should stop an active video session before stopping the active RTSP session. It should remain a no-op when nothing is active.

- [x] **Step 5: Run GREEN native client tests**

Run the focused `GameStreamNativeStreamClientTest` and confirm it passes.

Result: the focused `GameStreamNativeStreamClientTest` run passed.

### Task 5: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-97-gamestream-video-session-hook.md`

- [x] **Step 1: Update docs**

Document that Android now has a structured RTSP session-info contract and an optional fakeable video-session hook, while UDP/RTP packet intake and media decode remain future work.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout/connect-timeout patterns.

Result: `git diff --check` passed, and the diff scan reported `NO_MATCHES`.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk
adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity
```

Expected: Android tests/APK build, .NET solution tests, and emulator launch smoke pass.

Result: `gradle --no-daemon -p src\Beacon.Android test assembleDebug` passed; `dotnet test Beacon.slnx` passed with 296 total .NET tests; emulator reinstall reported `Success` and launch of `dev.beacon.android/.BeaconActivity` reported `Status: ok`.

- [ ] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.

### Task 6: Post-Sync Video Hook Cleanup Hardening

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamNativeStreamClientTest.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamNativeStreamClient.java`
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-97-gamestream-video-session-hook.md`

- [x] **Step 1: Add RED video-hook cleanup exception tests**

Add tests proving that a video-session startup exception returns a diagnostic and releases RTSP, and that a video-session stop exception does not prevent RTSP release.

Result: the focused `GameStreamNativeStreamClientTest` run failed in `videoSessionStartExceptionStopsVideoAndRtspSessionAndReturnsDiagnostic` and `stopReleasesRtspWhenVideoStopThrows` because the exceptions escaped.

- [x] **Step 2: Harden video-hook startup and stop cleanup**

Catch unchecked video hook startup failures, ask the video hook to clean up, release RTSP, and return a diagnostic. During stop, contain unchecked video stop failures so RTSP still stops.

Result: the focused `GameStreamNativeStreamClientTest` run passed.

- [x] **Step 3: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout/connect-timeout patterns.

Result: `git diff --check` passed, and the diff scan reported `NO_MATCHES`.

- [x] **Step 4: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk
adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity
```

Expected: Android tests/APK build, .NET solution tests, and emulator launch smoke pass.

Result: `gradle --no-daemon -p src\Beacon.Android test assembleDebug` passed; `dotnet test Beacon.slnx` passed with 296 total .NET tests; emulator reinstall reported `Success` and launch of `dev.beacon.android/.BeaconActivity` reported `Status: ok`.

- [ ] **Step 5: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
