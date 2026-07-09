# Milestone 89: Framed Sample Transport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Beacon-owned framed encoded-video sample transport so Android can consume timestamped samples from an advertised transport endpoint instead of deriving all timing locally from a raw H.264 file.

**Architecture:** Keep GameStream/Moonlight protocol implementation out of this milestone. The beacon-test backend continues to expose the raw H.264 diagnostic asset, and also advertises a `samples` endpoint using a small Beacon binary envelope. The server envelope is deterministic and testable: magic `BEACONANNEXB1\n`, then repeated little-endian records of `presentationTimeUs:int64`, `sampleLength:int32`, and Annex B sample bytes. Android prefers the `samples` endpoint when metadata says `sampleTransport=beacon-annexb-samples`, parses framed records into `EncodedVideoSample`s, and falls back to the raw Annex B splitter for older descriptors.

**Tech Stack:** ASP.NET minimal APIs, Beacon test streaming backend, Java Android stream descriptor parsing, JVM unit tests, xUnit API tests.

---

### Task 1: RED Server Descriptor And Sample Endpoint Tests

**Files:**
- Modify: `tests/Beacon.Core.Tests/Streaming/BeaconTestStreamingBackendTests.cs`
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`

- [x] **Step 1: Strengthen encoded-video descriptor assertions**

Update encoded-video descriptor tests so they expect:
- endpoint role `video` with `/streams/beacon-test/color-bars.h264`.
- endpoint role `samples` with `/streams/beacon-test/color-bars.beacon-annexb`.
- metadata `sampleTransport=beacon-annexb-samples`.

- [x] **Step 2: Add framed sample endpoint API test**

Add `BeaconTestEncodedVideoSampleStreamReturnsFramedSamples` to `ClientApiTests`. It should call `GET /streams/beacon-test/color-bars.beacon-annexb`, assert `200`, content type `application/vnd.beacon.annexb-samples`, magic prefix `BEACONANNEXB1\n`, and at least two records with non-empty sample lengths.

- [x] **Step 3: Run RED server tests**

Run:

```powershell
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter FullyQualifiedName~BeaconTestStreamingBackendTests
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "FullyQualifiedName~ClientApiTests|FullyQualifiedName~BeaconServiceRegistrationTests"
```

Expected: fail because the descriptor does not include `samples`/`sampleTransport`, and the framed sample endpoint returns 404.

Observed RED:
- `dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter FullyQualifiedName~BeaconTestStreamingBackendTests` failed with endpoint count `Expected: 2`, `Actual: 1`.
- `dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "FullyQualifiedName~ClientApiTests|FullyQualifiedName~BeaconServiceRegistrationTests"` failed with endpoint count `Expected: 2`, `Actual: 1`, and `BeaconTestEncodedVideoSampleStreamReturnsFramedSamples` returned `NotFound`.

### Task 2: GREEN Server Framed Sample Transport

**Files:**
- Modify: `src/Beacon.Core/Streaming/BeaconTestStreamingBackend.cs`
- Create: `src/Beacon.Server/Api/AnnexBAccessUnitSplitter.cs`
- Create: `src/Beacon.Server/Api/BeaconAnnexBSampleEnvelope.cs`
- Modify: `src/Beacon.Server/Api/StreamAssetEndpoints.cs`

- [x] **Step 1: Advertise the sample endpoint**

Add `BeaconTestStreamingBackend.EncodedVideoSamplesEndpoint = "/streams/beacon-test/color-bars.beacon-annexb"`. For encoded-video mode, return both `video` and `samples` endpoints and add metadata `sampleTransport=beacon-annexb-samples`.

- [x] **Step 2: Add server Annex B splitter**

Add a server-side splitter matching the Android diagnostic rules: accept `00 00 00 01` and `00 00 01` start codes, keep SPS/PPS and other non-VCL NAL units with the following VCL sample, and start a new sample when a second VCL NAL begins.

- [x] **Step 3: Add sample envelope writer**

Add `BeaconAnnexBSampleEnvelope.Create(byte[] annexBBytes, int fps)` that writes ASCII magic `BEACONANNEXB1\n`, splits access units, and writes little-endian records of PTS and sample length. PTS increments by `1_000_000 / fps`.

- [x] **Step 4: Map framed sample endpoint**

Map `GET /streams/beacon-test/color-bars.beacon-annexb`; read the same H.264 asset, envelope it with `fps=120`, and return content type `application/vnd.beacon.annexb-samples`.

- [x] **Step 5: Run GREEN server tests**

Run the focused tests from Task 1 and confirm they pass.

Observed GREEN:
- `dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter FullyQualifiedName~BeaconTestStreamingBackendTests` passed: 5 passed.
- `dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "FullyQualifiedName~ClientApiTests|FullyQualifiedName~BeaconServiceRegistrationTests"` passed: 71 passed.

### Task 3: RED Android Framed Transport Contract Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/EncodedVideoStreamPlanTest.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/HttpEncodedVideoSampleProviderFactoryTest.java`
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconAnnexBSampleEnvelopeParserTest.java`

- [x] **Step 1: Add stream-plan sample endpoint test**

Add a test proving `EncodedVideoStreamPlan` reads `metadata.sampleTransport=beacon-annexb-samples`, finds endpoint role `samples`, and exposes that endpoint for the sample provider.

- [x] **Step 2: Add envelope parser tests**

Create tests for a small framed byte array with two records. Assert parsed samples preserve PTS values and sample bytes. Add a corrupt-magic test that throws `Encoded video sample stream is not a Beacon Annex B sample envelope.`

- [x] **Step 3: Add provider framed-transport test**

Add a provider test where the descriptor advertises `sampleTransport=beacon-annexb-samples`, the `samples` endpoint is fetched, and returned samples use PTS values from the envelope rather than locally derived FPS timing.

- [x] **Step 4: Run RED Android tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.EncodedVideoStreamPlanTest --tests dev.beacon.android.HttpEncodedVideoSampleProviderFactoryTest --tests dev.beacon.android.BeaconAnnexBSampleEnvelopeParserTest
```

Expected: fail because the plan does not expose sample transport/sample URI and the parser does not exist.

Observed RED:
- The focused Gradle command failed at `:app:compileDebugUnitTestJavaWithJavac` because `BeaconAnnexBSampleEnvelopeParser`, `EncodedVideoStreamPlan.sampleTransport()`, and `EncodedVideoStreamPlan.sampleUri()` did not exist.

### Task 4: GREEN Android Framed Transport Consumption

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/EncodedVideoStreamPlan.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconAnnexBSampleEnvelopeParser.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/HttpEncodedVideoSampleProviderFactory.java`

- [x] **Step 1: Extend encoded-video plan**

Add `sampleTransport()` and `sampleUri()` accessors. `sampleUri` should use endpoint role `samples` when `sampleTransport=beacon-annexb-samples`; otherwise it should remain empty and the provider should use `videoUri`.

- [x] **Step 2: Add Android envelope parser**

Parse magic `BEACONANNEXB1\n`, then little-endian PTS/length/sample records. Reject bad magic and truncated records with clear diagnostics.

- [x] **Step 3: Prefer framed sample transport in provider**

If `plan.sampleTransport()` is `beacon-annexb-samples`, fetch `plan.sampleUri()`, parse the envelope, and return those samples. Otherwise keep the existing raw Annex B split path.

- [x] **Step 4: Run GREEN Android tests**

Run the focused Android tests from Task 3 and confirm they pass.

Observed GREEN:
- The focused Gradle command passed for `EncodedVideoStreamPlanTest`, `HttpEncodedVideoSampleProviderFactoryTest`, and `BeaconAnnexBSampleEnvelopeParserTest`.
- Additional fallback hardening: `AnnexBAccessUnitSplitterTest.acceptsThreeByteStartCodesAfterFourByteParameterSets` was added after discovering the diagnostic asset mixes 4-byte SPS/PPS start codes with 3-byte VCL start codes. It failed before the Android raw fallback splitter was updated, then passed after the splitter began tracking both start-code lengths.

### Task 5: Documentation, Emulator Smoke, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-89-framed-sample-transport.md`

- [x] **Step 1: Update README**

Document that beacon-test encoded-video advertises both raw H.264 and a Beacon framed Annex B sample endpoint, and Android prefers the framed endpoint when advertised. Keep the warning that full GameStream/Moonlight transport remains future work.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout"
```

Expected: no whitespace errors and no new timeout/cancellation patterns.

Observed:
- `git diff --check` passed.
- `git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout"` found no timeout/cancellation patterns.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
```

Expected: Android tests/APK build and .NET solution tests pass.

Observed:
- Gradle `test assembleDebug` passed.
- `dotnet test Beacon.slnx` passed across all .NET test projects.
- After the Android raw fallback splitter hardening, Gradle `test assembleDebug` and `dotnet test Beacon.slnx` were rerun and passed again.

- [x] **Step 4: Emulator smoke**

Run beacon-test encoded-video mode on `127.0.0.1:5000`, install/start the debug APK, launch the test stream, and verify:
- `/streams/beacon-test/color-bars.beacon-annexb` returns `200` and the Beacon envelope content type.
- server `/admin/snapshot` stream state is `running`.
- APK status includes `Native encoded video stream started`.
- logcat has no `FATAL EXCEPTION`, `CodecException`, `Cleartext HTTP traffic`, `Launch failed`, or `Encoded video surface is not ready`.

Observed: server `/health` returned `{"status":"ok"}` on `127.0.0.1:5000`; `/streams/beacon-test/color-bars.beacon-annexb` returned `200 application/vnd.beacon.annexb-samples` with `188941` bytes; `/streams/beacon-test/color-bars.h264` returned `200 video/H264` with `185327` bytes. The rebuilt APK reported `Native encoded video stream started. codec=h264 container=annex-b video=/streams/beacon-test/color-bars.h264 2560x1600@120`; server `/admin/snapshot` reported the stream state as `running`, `error=null`, `activeSessions=1`, and advertised the `samples=/streams/beacon-test/color-bars.beacon-annexb` endpoint with `sampleTransport=beacon-annexb-samples`; logcat had zero hits for all required failure signatures. The smoke stream was stopped through `POST /admin/clients/z-fold-7/stream/stop` and the local server was stopped after validation.

- [ ] **Step 5: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
