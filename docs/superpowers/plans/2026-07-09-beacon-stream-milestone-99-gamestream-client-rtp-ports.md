# Milestone 99: GameStream Client RTP Ports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Android GameStream RTSP SETUP advertise explicit per-stream client RTP ports and retain those client ports in session info for the future UDP RTP packet source.

**Architecture:** Keep the current diagnostic/static port allocation for now, but move it out of a hidden `RtspRequest` magic constant and into the GameStream handshake boundary. `RtspRequest.setup(...)` accepts the selected client RTP port, and `GameStreamRtspSessionInfo` stores both local client and remote server ports. Dynamic socket/port leasing and UDP receive remain the next milestone.

**Tech Stack:** Java Android RTSP boundaries, JVM unit tests, README/extraction-map documentation, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED Explicit RTSP Client Port Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtspRequestTest.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspHandshakeClientTest.java`

- [x] **Step 1: Update RTSP SETUP request tests**

Change the setup request tests to call `RtspRequest.setup(..., clientRtpPort)` and assert the `Transport` header uses that port pair, for example `50002-50003` for video.

- [x] **Step 2: Update handshake request tests**

Assert the audio SETUP request advertises `X-GS-ClientPort=50000-50001`, video advertises `50002-50003`, and control advertises `50004-50005`.

- [x] **Step 3: Update session-info assertions**

Assert `GameStreamRtspSessionInfo` retains `audioClientPort=50000`, `videoClientPort=50002`, and `controlClientPort=50004` alongside the existing server ports.

- [x] **Step 4: Run RED tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.RtspRequestTest --tests dev.beacon.android.GameStreamRtspHandshakeClientTest
```

Expected: fail because `RtspRequest.setup(...)` does not accept explicit client ports and session info does not expose client-port getters.

Result: the focused Gradle run failed at `:app:compileDebugUnitTestJavaWithJavac` because the explicit `RtspRequest.setup(..., clientRtpPort)` overload and `GameStreamRtspSessionInfo` client-port getters did not exist.

### Task 2: GREEN Explicit RTSP Client Ports

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtspRequest.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspHandshakeClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSessionInfo.java`

- [x] **Step 1: Add explicit SETUP client port support**

Add `RtspRequest.setup(String target, int cseq, String host, String sessionId, int clientRtpPort)` and generate `Transport: unicast;X-GS-ClientPort=<port>-<port+1>`. Keep the existing overload as a compatibility wrapper using `50000`.

- [x] **Step 2: Add session-info client ports**

Add `audioClientPort()`, `videoClientPort()`, and `controlClientPort()` getters. Keep the existing `started(...)` factory compatible with older tests by setting client ports to `-1`, and add `startedWithClientPorts(...)` for the handshake path.

- [x] **Step 3: Make handshake own the static client ports**

Add static client RTP port constants in `GameStreamRtspHandshakeClient`: audio `50000`, video `50002`, control `50004`. Pass them to `RtspRequest.setup(...)` and to `GameStreamRtspSessionInfo.startedWithClientPorts(...)`.

- [x] **Step 4: Run GREEN focused tests**

Run the focused `RtspRequestTest` and `GameStreamRtspHandshakeClientTest` command again and confirm it passes.

Result: the focused Gradle run passed.

### Task 3: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-99-gamestream-client-rtp-ports.md`

- [x] **Step 1: Update docs**

Document that Milestone 99 makes Android retain RTSP-advertised client RTP ports for future UDP packet intake, while dynamic port leasing and UDP reads are still future work.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout/connect-timeout patterns.

Result: `git diff --check` passed, and the diff scan returned `NO_MATCHES`.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk
adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity
```

Expected: Android tests/APK build, .NET solution tests, and emulator launch smoke pass.

Result: `gradle --no-daemon -p src\Beacon.Android test assembleDebug` passed, `dotnet test Beacon.slnx` passed with 296 total .NET tests, `adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk` returned `Success`, and `adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity` returned `Status: ok` with `LaunchState: COLD`.

- [ ] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
