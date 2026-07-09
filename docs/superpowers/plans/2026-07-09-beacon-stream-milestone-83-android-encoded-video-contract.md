# Milestone 83: Android Encoded Video Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an Android-readable encoded-video stream contract so the APK can distinguish diagnostic color bars from decoder-bound video descriptors before MediaCodec transport work.

**Architecture:** Preserve existing `beacon-test://pattern/color-bars` behavior. Extend Android `StreamConnectionDescriptor` to parse server-provided `metadata`, add a pure `EncodedVideoStreamPlan` validator for `streamKind=encoded-video`, and add an `EncodedVideoNativeStreamClient` behind the native stream router. The encoded client validates the contract and returns a truthful decoder-not-implemented diagnostic instead of pretending frames are decoded.

**Tech Stack:** Java Android JVM tests, existing Beacon server/core streaming descriptors, no physical phone required.

---

## Requirements

- `REQ-CTRL-013`: endpoint-only descriptors must produce explicit client behavior or diagnostics.
- `REQ-NET-002`: the APK reports/uses decode capability facts without inventing unsupported decode behavior.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-TEST-010`: real phone testing remains final confirmation for actual decoder compatibility and stream quality.
- `REQ-SYNC-006`: docs, implementation, and validation status stay aligned.

## Non-Goals

- No MediaCodec decode loop.
- No network transport implementation.
- No GameStream/Moonlight protocol implementation.
- No SurfaceView or renderer lifecycle change.
- No timeout, polling, or cancellation behavior.

## Encoded Video Contract

A server response can describe decoder-bound test video as:

```json
{
  "stream": {
    "connection": {
      "protocol": "beacon-test",
      "endpoints": [
        { "role": "video", "uri": "beacon-test://video/color-bars.h264" }
      ],
      "metadata": {
        "streamKind": "encoded-video",
        "codec": "h264",
        "container": "annex-b",
        "width": "1280",
        "height": "720",
        "fps": "60"
      }
    }
  }
}
```

Android must reject incomplete contracts with precise diagnostics. A valid contract is still diagnostic-only until a MediaCodec client replaces the preflight client.

## Files

- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/StreamConnectionDescriptor.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoStreamPlan.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoNativeStreamClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/DiagnosticNativeStreamClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconTestNativeStreamClient.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/StreamConnectionDescriptorTest.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/EncodedVideoStreamPlanTest.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/EncodedVideoNativeStreamClientTest.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/DiagnosticNativeStreamClientTest.java`
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Modify: `README.md`

## Tasks

### Task 1: RED Android metadata and encoded-video tests

- [x] **Step 1: Write failing JVM tests**

Add tests proving:

- `StreamConnectionDescriptor` preserves string/number/boolean metadata values and ignores blank metadata keys.
- `EncodedVideoStreamPlan` accepts only `protocol=beacon-test`, `streamKind=encoded-video`, a `video` endpoint, supported codecs `h264`, `hevc`, or `av1`, supported containers `annex-b` or `mp4`, and positive width/height/fps metadata.
- `EncodedVideoNativeStreamClient` returns a truthful diagnostic for a valid encoded-video contract: `Beacon encoded video contract is valid, but MediaCodec decode is not implemented yet. codec=h264 container=annex-b video=beacon-test://video/color-bars.h264 1280x720@60`.
- `DiagnosticNativeStreamClient` routes encoded-video descriptors before the color-bars client so encoded video does not fall through to the old pattern diagnostic.

- [x] **Step 2: Run RED tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.StreamConnectionDescriptorTest --tests dev.beacon.android.EncodedVideoStreamPlanTest --tests dev.beacon.android.EncodedVideoNativeStreamClientTest --tests dev.beacon.android.DiagnosticNativeStreamClientTest
```

Expected: compile/test failure because metadata parsing and encoded-video classes do not exist yet.

Observed: focused Gradle tests failed at compile time because `metadataValue`, `metadata`, `EncodedVideoStreamPlan`, and `EncodedVideoNativeStreamClient` were missing.

### Task 2: GREEN Android encoded-video contract

- [x] **Step 1: Parse connection metadata**

Extend `StreamConnectionDescriptor` with:

- `Map<String, String> metadata`;
- `metadata()` accessor returning an unmodifiable map;
- `metadataValue(String key)` returning trimmed values or empty string;
- parser support for JSON primitive metadata values.

- [x] **Step 2: Implement `EncodedVideoStreamPlan`**

Add a pure validator over `StreamConnectionDescriptor` that exposes `supportedProtocol()`, `complete()`, `diagnostic()`, `videoUri()`, `codec()`, `container()`, `width()`, `height()`, and `fps()`.

- [x] **Step 3: Implement `EncodedVideoNativeStreamClient`**

Add a `NativeStreamProtocolClient` that supports `EncodedVideoStreamPlan.supportedProtocol()`, returns the precise missing-contract diagnostic for incomplete descriptors, and returns the decoder-not-implemented diagnostic for complete descriptors.

- [x] **Step 4: Wire router before color bars**

Update `DiagnosticNativeStreamClient` default composition to order:

1. `EncodedVideoNativeStreamClient`
2. `BeaconTestNativeStreamClient`
3. `GameStreamNativeStreamClient`

Update `BeaconTestNativeStreamClient.supports()` so it ignores encoded-video contracts and preserves the existing color-bars behavior.

- [x] **Step 5: Run GREEN tests**

Run the Task 1 Gradle command again. Expected: PASS.

Observed: focused metadata and encoded-video Android tests passed.

### Task 3: Server/docs alignment and validation

- [x] **Step 1: Assert beacon-test metadata in server API**

Extend `LaunchCanReturnBeaconTestEndpointOnlyStreamConnection` to assert existing `metadata.pattern=color-bars`, proving server metadata is visible to clients.

- [x] **Step 2: Update README**

Document the Android-readable encoded-video contract, and state that it is currently preflight/diagnostic-only until MediaCodec decode is implemented.

- [x] **Step 3: Validate**

Run:

```powershell
git diff --check
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
dotnet test Beacon.slnx --no-build
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: all commands pass; `rg` should return no matches.

Observed: `git diff --check`, the timeout/cancellation search, `dotnet test Beacon.slnx --no-build`, and Android `test assembleDebug` all passed.

- [ ] **Step 4: Commit and PR**

Commit with:

```powershell
git add README.md docs\superpowers\plans\2026-07-09-beacon-stream-milestone-83-android-encoded-video-contract.md src\Beacon.Android\app\src\main\java\dev\beacon\android\StreamConnectionDescriptor.java src\Beacon.Android\app\src\main\java\dev\beacon\android\EncodedVideoStreamPlan.java src\Beacon.Android\app\src\main\java\dev\beacon\android\EncodedVideoNativeStreamClient.java src\Beacon.Android\app\src\main\java\dev\beacon\android\DiagnosticNativeStreamClient.java src\Beacon.Android\app\src\main\java\dev\beacon\android\BeaconTestNativeStreamClient.java src\Beacon.Android\app\src\test\java\dev\beacon\android\StreamConnectionDescriptorTest.java src\Beacon.Android\app\src\test\java\dev\beacon\android\EncodedVideoStreamPlanTest.java src\Beacon.Android\app\src\test\java\dev\beacon\android\EncodedVideoNativeStreamClientTest.java src\Beacon.Android\app\src\test\java\dev\beacon\android\DiagnosticNativeStreamClientTest.java tests\Beacon.Server.Tests\ClientApiTests.cs
git commit -m "Add Android encoded video stream contract"
```

Push, open a PR, verify CI, and merge when green.
