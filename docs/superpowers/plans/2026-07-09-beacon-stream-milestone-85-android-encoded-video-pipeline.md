# Milestone 85: Android Encoded Video Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Android's hardcoded encoded-video "decode not implemented" endpoint with a fakeable decoder pipeline boundary that can start a decoder-backed native stream from the server descriptor.

**Architecture:** Keep the default APK behavior truthful when no decoder is configured. Add a small `EncodedVideoDecoder` interface and request/result types, let `EncodedVideoNativeStreamClient` delegate valid encoded-video contracts to that decoder, and return an encoded-video presentation when the decoder starts. JVM tests use a recording fake decoder; real Android MediaCodec and Surface rendering remain the next milestone.

**Tech Stack:** Java Android app, existing native stream router, JVM unit tests, README docs.

---

### Task 1: RED Decoder Pipeline Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/EncodedVideoNativeStreamClientTest.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/DiagnosticNativeStreamClientTest.java`

- [x] **Step 1: Write failing decoder success test**

Add a test that creates `EncodedVideoNativeStreamClient` with a recording fake `EncodedVideoDecoder`, starts `EncodedVideoNativeStreamClientTest.validConnection()`, and asserts:
- the result succeeds.
- the fake decoder received a request whose plan has codec `h264`, container `annex-b`, video URI `beacon-test://video/color-bars.h264`, width `1280`, height `720`, and fps `60`.
- the native stream status is `Native encoded video stream started. codec=h264 container=annex-b video=beacon-test://video/color-bars.h264 1280x720@60`.
- the presentation is active with kind `encoded-video` and the same endpoint URI.

- [x] **Step 2: Write failing stop/diagnostic tests**

Add tests proving:
- `stop()` calls the configured decoder only after a successful start.
- incomplete encoded-video contracts do not call the decoder.
- the default client still returns the truthful MediaCodec-not-configured diagnostic so it does not pretend decode works before the real Android decoder is wired.
- the default facade still routes encoded-video descriptors before color-bars and surfaces the same truthful diagnostic.

- [x] **Step 3: Run RED Android tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.EncodedVideoNativeStreamClientTest --tests dev.beacon.android.DiagnosticNativeStreamClientTest
```

Expected: compile failure because `EncodedVideoDecoder`, decoder request/result types, and `NativeStreamPresentation.encodedVideo(...)` do not exist.

Observed: compile failed because `EncodedVideoDecoder`, `EncodedVideoDecodeRequest`, `EncodedVideoDecodeResult`, and the configured decoder constructor did not exist.

### Task 2: GREEN Decoder Pipeline

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoDecoder.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoDecodeRequest.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoDecodeResult.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoNativeStreamClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/NativeStreamPresentation.java`

- [x] **Step 1: Add decoder contracts**

Add:
- `EncodedVideoDecoder.start(EncodedVideoDecodeRequest request)`
- `EncodedVideoDecoder.stop()`
- immutable `EncodedVideoDecodeRequest` containing the parsed `EncodedVideoStreamPlan`
- immutable `EncodedVideoDecodeResult` with `started(status)` and `failed(diagnostic)` factories.

- [x] **Step 2: Add encoded-video presentation**

Add `NativeStreamPresentation.encodedVideo(String endpointUri, String codec, int width, int height, int fps)` returning kind `encoded-video` with a readable label.

- [x] **Step 3: Delegate valid plans to the decoder**

Change `EncodedVideoNativeStreamClient` so:
- default construction uses an internal decoder that returns the existing truthful diagnostic.
- configured construction delegates complete contracts to the decoder.
- decoder failures return `NativeStreamStartResult.unsupported(...)`.
- decoder success returns `NativeStreamStartResult.started(...)` with encoded-video presentation.
- `stop()` calls the configured decoder only after a successful start.

- [x] **Step 4: Run GREEN Android tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.EncodedVideoNativeStreamClientTest --tests dev.beacon.android.DiagnosticNativeStreamClientTest
```

Expected: selected Android tests pass.

Observed: selected Android tests passed.

### Task 3: Docs And Validation

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-85-android-encoded-video-pipeline.md`

- [x] **Step 1: Update README**

Document that milestone 85 adds a fakeable encoded-video decoder pipeline boundary. State clearly that the default APK still reports no configured MediaCodec decoder until the real Android decoder and Surface rendering milestone lands.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout"
```

Expected: no whitespace errors; no new timeout/cancellation pattern introduced.

Observed: `git diff --check` passed, and the diff-only timeout-pattern scan returned no matches.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
```

Expected: Android unit tests/APK build and .NET solution tests pass.

Observed: Android `test assembleDebug` passed, and `dotnet test Beacon.slnx` passed.

- [ ] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.

```powershell
git add README.md docs/superpowers/plans/2026-07-09-beacon-stream-milestone-85-android-encoded-video-pipeline.md src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoDecoder.java src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoDecodeRequest.java src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoDecodeResult.java src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoNativeStreamClient.java src/Beacon.Android/app/src/main/java/dev/beacon/android/NativeStreamPresentation.java src/Beacon.Android/app/src/test/java/dev/beacon/android/EncodedVideoNativeStreamClientTest.java src/Beacon.Android/app/src/test/java/dev/beacon/android/DiagnosticNativeStreamClientTest.java
git commit -m "Add Android encoded video decoder pipeline"
git push -u origin codex/milestone-85-android-encoded-video-pipeline
gh pr create --title "Add Android encoded video decoder pipeline" --body "Adds a fakeable Android encoded-video decoder boundary so valid server descriptors can enter a decoder-backed native stream path while default behavior remains truthful until MediaCodec rendering lands."
```
