# Milestone 86: Android MediaCodec Surface Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire Android encoded-video streams to a real MediaCodec/Surface lifecycle boundary while keeping sample transport and frame feeding as the next milestone.

**Architecture:** Add a JVM-testable `SurfaceEncodedVideoDecoder` coordinator behind the existing `EncodedVideoDecoder` interface. The coordinator depends on fakeable codec factory/codec/surface-provider interfaces, so unit tests can prove surface readiness, configure/start, failure cleanup, and stop/release behavior. Production Android code supplies an `AndroidMediaCodecFactory` adapter and a `SurfaceView` provider from `BeaconActivity`.

**Tech Stack:** Java Android app, Android `MediaCodec`, `MediaFormat`, `SurfaceView`, JVM unit tests, existing native stream router.

---

### Task 1: RED Surface Decoder Lifecycle Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/SurfaceEncodedVideoDecoderTest.java`

- [x] **Step 1: Write failing surface/codec lifecycle tests**

Add tests proving:
- start fails with diagnostic `Encoded video surface is not ready.` when no surface is available.
- start creates a codec for the plan codec, configures it with the plan and surface, starts it, and returns a successful status.
- stop stops and releases the active codec once.
- if codec configure/start fails, the codec is released and the diagnostic is returned.

- [x] **Step 2: Run RED tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.SurfaceEncodedVideoDecoderTest
```

Expected: compile failure because `SurfaceEncodedVideoDecoder`, codec factory, codec, and surface provider interfaces do not exist yet.

Observed: compile failed because `SurfaceEncodedVideoDecoder`, `EncodedVideoSurfaceProvider`, `EncodedVideoCodecFactory`, and `EncodedVideoCodec` did not exist.

### Task 2: GREEN Surface Decoder Coordinator

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoCodec.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoCodecFactory.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoSurfaceProvider.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/SurfaceEncodedVideoDecoder.java`

- [x] **Step 1: Add codec/surface interfaces**

Add:
- `EncodedVideoCodecFactory.create(String codec)`
- `EncodedVideoCodec.configure(EncodedVideoStreamPlan plan, Object surface)`
- `EncodedVideoCodec.start()`
- `EncodedVideoCodec.stop()`
- `EncodedVideoCodec.release()`
- `EncodedVideoSurfaceProvider.currentSurface()`

- [x] **Step 2: Add decoder coordinator**

Implement `SurfaceEncodedVideoDecoder`:
- read the current surface before creating a codec.
- fail with `Encoded video surface is not ready.` when surface is null.
- create/configure/start a codec for the plan codec.
- return `MediaCodec decoder configured. codec=<codec> container=<container> video=<uri> <width>x<height>@<fps>` on success.
- release the codec if configure/start throws.
- stop and release the active codec once.

- [x] **Step 3: Run GREEN lifecycle tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.SurfaceEncodedVideoDecoderTest
```

Expected: selected tests pass.

Observed: selected `SurfaceEncodedVideoDecoderTest` tests passed.

### Task 3: Android MediaCodec Adapter And Activity Wiring

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidMediaCodecFactory.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidSurfaceViewProvider.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
- Modify: `README.md`

- [x] **Step 1: Add Android MediaCodec adapter**

Add `AndroidMediaCodecFactory` that maps:
- `h264` to `MediaFormat.MIMETYPE_VIDEO_AVC`
- `hevc` to `MediaFormat.MIMETYPE_VIDEO_HEVC`
- `av1` to `MediaFormat.MIMETYPE_VIDEO_AV1`

The adapter must create a decoder by MIME type, configure it with `MediaFormat.createVideoFormat(...)`, frame rate, and an Android `Surface`, then start/stop/release through the fakeable codec interface.

- [x] **Step 2: Add SurfaceView provider**

Add `AndroidSurfaceViewProvider` that returns `surfaceView.getHolder().getSurface()` only when the surface exists and is valid.

- [x] **Step 3: Wire Activity native stream client**

Add a `SurfaceView` to `BeaconActivity`, keep it available for encoded-video rendering, and create the default `BeaconViewModel` with:
- `EncodedVideoNativeStreamClient(new SurfaceEncodedVideoDecoder(new AndroidMediaCodecFactory(), new AndroidSurfaceViewProvider(encodedVideoSurfaceView)))`
- existing `BeaconTestNativeStreamClient`
- existing `GameStreamNativeStreamClient`

Presentation updates should show the encoded-video surface only for encoded-video presentations and keep the color-bars view for color-bars presentations.

- [x] **Step 4: Update README**

Document that milestone 86 wires MediaCodec/Surface configure/start/release lifecycle, but encoded byte fetching and queueing still remain a later milestone.

### Task 4: Validation And Sync

**Files:**
- Verify all milestone files.

- [x] **Step 1: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout"
```

Expected: no whitespace errors; no new timeout/cancellation pattern introduced.

Observed: `git diff --check` passed, and the diff-only timeout-pattern scan returned no matches.

- [x] **Step 2: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.SurfaceEncodedVideoDecoderTest
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
```

Expected: selected lifecycle tests, Android full tests/APK build, and .NET solution tests pass.

Observed: selected `SurfaceEncodedVideoDecoderTest`, Android `test assembleDebug`, and `dotnet test Beacon.slnx` passed.

- [ ] **Step 3: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.

```powershell
git add README.md docs/superpowers/plans/2026-07-09-beacon-stream-milestone-86-android-mediacodec-surface-lifecycle.md src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoCodec.java src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoCodecFactory.java src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoSurfaceProvider.java src/Beacon.Android/app/src/main/java/dev/beacon/android/SurfaceEncodedVideoDecoder.java src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidMediaCodecFactory.java src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidSurfaceViewProvider.java src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java src/Beacon.Android/app/src/test/java/dev/beacon/android/SurfaceEncodedVideoDecoderTest.java
git commit -m "Wire Android MediaCodec surface lifecycle"
git push -u origin codex/milestone-86-android-mediacodec-surface-lifecycle
gh pr create --title "Wire Android MediaCodec surface lifecycle" --body "Adds a MediaCodec/Surface lifecycle boundary for Android encoded-video streams while leaving encoded byte transport and queueing for the next milestone."
```
