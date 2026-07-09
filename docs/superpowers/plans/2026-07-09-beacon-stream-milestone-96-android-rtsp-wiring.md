# Milestone 96: Android RTSP Wiring And Session Retention Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Android APK use the lifecycle-owned GameStream RTSP socket session client when it receives endpoint-only GameStream/Moonlight descriptors, and retain one `BeaconViewModel`/native-stream session across APK button actions so `Stop Stream` can release that session. This milestone proves APK RTSP control-session wiring and cleanup with emulator validation; it does not implement RTP video/audio/control media decode.

**Architecture:** Keep the JVM/default diagnostic facade available for unit tests and non-APK callers. Add a small APK native-stream factory that composes the existing encoded-video, beacon-test, and GameStream clients with an injected `GameStreamRtspSessionClient`. Add a pure Java view-model session cache so `BeaconActivity` can reuse the same model while server URL/client id remain unchanged and close native stream state when the identity changes or the Activity is destroyed. Activity actions already run on a single-thread executor; do not add sleeps, retry loops, timeout cancellation, socket timeouts, or connect timeouts.

**Tech Stack:** Java Android client composition helpers, JVM unit tests, README/extraction-map documentation, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED ViewModel Session Retention Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconViewModelSessionTest.java`

- [x] **Step 1: Add same identity reuses model test**

Use a factory that records created models. Request a model for the same server URL/client id twice and assert the same instance is returned and the factory only runs once.

- [x] **Step 2: Add identity change closes previous native stream test**

Start a native stream on the first model through a fake launch response, then request a model for a different server URL or client id. Assert the previous model's native stream client is stopped once before replacement.

- [x] **Step 3: Add close releases active native stream test**

Start a native stream, close the session cache, and assert the active native stream client is stopped once and the next request creates a new model.

- [x] **Step 4: Run RED session tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.BeaconViewModelSessionTest
```

Expected: fail because `BeaconViewModelSession` and the model-level native-stream close hook do not exist.

Result: the focused Gradle run failed at `:app:compileDebugUnitTestJavaWithJavac` because `BeaconViewModelSession` did not exist.

### Task 2: RED APK Native Stream Factory Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/AndroidNativeStreamClientFactoryTest.java`

- [x] **Step 1: Add GameStream route uses injected RTSP session test**

Build an APK native-stream client with a fake encoded-video decoder and a recording RTSP session client that succeeds. Start a complete GameStream endpoint map and assert the RTSP session client received the plan and the result reports the RTSP session start.

- [x] **Step 2: Add stop releases GameStream route test**

Start the same complete GameStream endpoint map successfully, call `stop()`, and assert the recording RTSP session client was stopped once.

- [x] **Step 3: Add beacon-test and encoded-video route preservation tests**

Assert the factory still routes beacon-test color bars and encoded-video descriptors before GameStream.

- [x] **Step 4: Run RED factory tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.AndroidNativeStreamClientFactoryTest
```

Expected: fail because `AndroidNativeStreamClientFactory` does not exist.

Result: the focused Gradle run failed at `:app:compileDebugUnitTestJavaWithJavac` because `AndroidNativeStreamClientFactory` and `BeaconViewModelSession` did not exist.

### Task 3: GREEN Session Retention And RTSP Wiring

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModelSession.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidNativeStreamClientFactory.java`

- [x] **Step 1: Add explicit native stream close hook**

Expose a package-visible or public method on `BeaconViewModel` that only clears the active native stream client state and does not call the server stop route.

- [x] **Step 2: Add `BeaconViewModelSession`**

Cache one model for the current server URL/client id. Reuse it for the same identity. When identity changes or the session closes, release native stream state on the previous model.

- [x] **Step 3: Add APK native stream factory**

Compose the existing router with configured encoded-video decoder, beacon-test client, and `GameStreamNativeStreamClient` backed by an injected `GameStreamRtspSessionClient`. Add a factory method for the real APK RTSP client using `GameStreamRtspTransportSessionClient(new RtspSocketTransportLeaseFactory(), GameStreamRtspSdpPayloadProvider.diagnostic())`.

- [x] **Step 4: Wire Activity through the session cache and APK factory**

Make `BeaconActivity` retain the view-model session across button actions, close it on destroy, and build the native stream client through the APK factory. Do not move display policy or stream planning into the APK.

- [x] **Step 5: Run GREEN focused tests**

Run the focused tests from Tasks 1-2 and confirm they pass.

Result: the focused Gradle run passed for `BeaconViewModelSessionTest` and `AndroidNativeStreamClientFactoryTest`.

### Task 4: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-96-android-rtsp-wiring.md`

- [x] **Step 1: Update docs**

Document that the APK now wires GameStream/Moonlight endpoint maps to the lifecycle-owned RTSP socket session client, while RTP media decode remains future work.

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
adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk
adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity
```

Expected: Android tests/APK build, .NET solution tests, and emulator launch smoke pass.

Result: `gradle --no-daemon -p src\Beacon.Android test assembleDebug` passed; `dotnet test Beacon.slnx` passed; emulator reinstall and launch of `dev.beacon.android/.BeaconActivity` reported `Status: ok`.

- [ ] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
