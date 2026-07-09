# Milestone 87: Encoded Video Byte Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the `beacon-test` encoded-video descriptor point at real server-served H.264 bytes and let Android fetch those bytes and queue them into the configured MediaCodec.

**Architecture:** Keep this as a diagnostic byte path, not a full GameStream protocol. The server exposes a deterministic Annex B H.264 test asset through a normal HTTP route and advertises that route as the encoded-video endpoint. Android adds fakeable sample-provider boundaries that resolve relative stream URIs against the configured Beacon server, prefetch a bounded diagnostic sample, and pass an in-memory sample provider to the MediaCodec adapter. The Android codec adapter uses callback-driven input/output handling so queueing does not depend on polling loops or cancellation timeouts.

**Tech Stack:** ASP.NET minimal APIs, Beacon test streaming backend, Java Android app, Android `MediaCodec.Callback`, JVM unit tests, xUnit API tests.

---

### Task 1: RED Server Encoded Asset Tests

**Files:**
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Modify: `tests/Beacon.Core.Tests/Streaming/BeaconTestStreamingBackendTests.cs`
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`

- [x] **Step 1: Write failing descriptor tests**

Update encoded-video assertions so the video endpoint is `/streams/beacon-test/color-bars.h264` instead of `beacon-test://video/color-bars.h264`.

- [x] **Step 2: Write failing HTTP asset test**

Add an API test that `GET /streams/beacon-test/color-bars.h264` returns `200`, `video/H264`, non-empty bytes, and an Annex B start code prefix.

- [x] **Step 3: Run RED server tests**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "FullyQualifiedName~ClientApiTests|FullyQualifiedName~BeaconServiceRegistrationTests"
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter FullyQualifiedName~BeaconTestStreamingBackendTests
```

Expected: descriptor tests fail with the old custom URI, and the asset route test fails with 404.

Observed: `BeaconTestStreamingBackendTests` failed because production still returned `beacon-test://video/color-bars.h264`; `ClientApiTests` and `BeaconServiceRegistrationTests` failed for the same descriptor mismatch plus 404 for `/streams/beacon-test/color-bars.h264`.

### Task 2: GREEN Server Encoded Asset Route

**Files:**
- Create: `src/Beacon.Server/Api/StreamAssetEndpoints.cs`
- Create: `src/Beacon.Server/Assets/beacon-test-color-bars.h264`
- Modify: `src/Beacon.Core/Streaming/BeaconTestStreamingBackend.cs`
- Modify: `src/Beacon.Server/Beacon.Server.csproj`
- Modify: `src/Beacon.Server/Program.cs`

- [x] **Step 1: Add deterministic H.264 asset**

Add a small Annex B H.264 byte asset generated from `smptebars` at `2560x1600` and copy it to the server output.

- [x] **Step 2: Add stream asset endpoint**

Map `GET /streams/beacon-test/color-bars.h264` and return the copied diagnostic asset with content type `video/H264`.

- [x] **Step 3: Update beacon-test encoded-video descriptor**

Change the encoded-video endpoint constant to `/streams/beacon-test/color-bars.h264`.

- [x] **Step 4: Run GREEN server tests**

Run the same focused .NET tests from Task 1 and confirm they pass.

Observed: focused `BeaconTestStreamingBackendTests`, `ClientApiTests`, and `BeaconServiceRegistrationTests` passed.

### Task 3: RED Android Sample Provider And Queueing Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/SurfaceEncodedVideoDecoderTest.java`
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/HttpEncodedVideoSampleProviderFactoryTest.java`

- [x] **Step 1: Write failing codec queueing test**

Extend the surface decoder test so a configured sample-provider factory is invoked with the stream plan and passed to the codec before start.

- [x] **Step 2: Write failing HTTP source tests**

Add JVM tests proving the HTTP sample provider:
- resolves `/streams/beacon-test/color-bars.h264` against `http://10.0.2.2:5000`.
- rejects unsupported non-HTTP absolute schemes with a clear diagnostic.
- returns one sample containing the fetched bytes and then an end-of-stream sample.

- [x] **Step 3: Run RED Android tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.SurfaceEncodedVideoDecoderTest --tests dev.beacon.android.HttpEncodedVideoSampleProviderFactoryTest
```

Expected: compile failure because sample provider types and HTTP sample provider factory do not exist yet.

Observed: Android test compilation failed because `EncodedVideoSample`, `EncodedVideoSampleProvider`, `EncodedVideoSampleProviderFactory`, and `HttpEncodedVideoSampleProviderFactory` did not exist and `SurfaceEncodedVideoDecoder` did not yet accept a sample-provider factory.

### Task 4: GREEN Android Sample Provider And MediaCodec Queueing

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoSample.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoSampleProvider.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoSampleProviderFactory.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/HttpEncodedVideoSampleProviderFactory.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoCodec.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/SurfaceEncodedVideoDecoder.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidMediaCodecFactory.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`

- [x] **Step 1: Add sample provider contracts**

Add immutable samples, an in-memory provider that returns samples then EOS, and a factory interface.

- [x] **Step 2: Add HTTP sample provider**

Resolve relative paths against the Beacon server URL, fetch bytes through a fakeable byte fetcher, reject unsupported schemes, and return a single diagnostic sample plus EOS.

- [x] **Step 3: Pass sample provider through decoder coordinator**

Create the provider before codec configure, pass it into `EncodedVideoCodec.configure(...)`, then start the codec.

- [x] **Step 4: Queue samples in Android MediaCodec callback**

Use `MediaCodec.Callback` to queue input buffers from the provider, queue EOS when samples are exhausted, release output buffers with rendering enabled, and release codec resources on stop.

- [x] **Step 5: Wire Activity to server-relative HTTP source**

Construct `HttpEncodedVideoSampleProviderFactory` with the current `serverUrl` when creating the native encoded-video decoder.

- [x] **Step 6: Run GREEN Android tests**

Run the focused Android tests from Task 3 and confirm they pass.

Observed: focused `SurfaceEncodedVideoDecoderTest` and `HttpEncodedVideoSampleProviderFactoryTest` passed.

Observed emulator smoke: first launch failed because emulator HTTP traffic to `10.0.2.2` was blocked by Android cleartext policy. Added `AndroidManifestNetworkPolicyTest`, verified it RED against the missing manifest attribute, then enabled cleartext for this local diagnostic client and verified the test GREEN.

Observed emulator smoke: second launch reached the server but failed with `Encoded video surface is not ready.` The `SurfaceView` had no usable height in the visible hierarchy. Keeping it attached with alpha toggling and an explicit `360` px layout height made the surface available without adding app-side timeouts.

Observed emulator smoke after the surface fix: Launch returned HTTP 200, server stream state was `running`, and the APK reported `Native encoded video stream started. codec=h264 container=annex-b video=/streams/beacon-test/color-bars.h264 2560x1600@120`. Logcat had no hits for `FATAL EXCEPTION`, `CodecException`, `Cleartext HTTP traffic`, `Launch failed`, or `Encoded video surface is not ready`.

### Task 5: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-87-encoded-video-byte-path.md`

- [x] **Step 1: Update README**

Document that `beacon-test` encoded-video now serves a real deterministic H.264 diagnostic asset and Android fetches/queues that asset into MediaCodec, while full GameStream/Moonlight transport remains future work.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout"
```

Expected: no whitespace errors and no new timeout/cancellation patterns.

Observed: `git diff --check` passed. The timeout/cancellation pattern scan returned no matches in the current diff.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
```

Expected: Android tests/APK build and .NET solution tests pass.

Observed: Gradle `test assembleDebug` passed. `dotnet test Beacon.slnx` passed with all .NET test projects green.

- [ ] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
