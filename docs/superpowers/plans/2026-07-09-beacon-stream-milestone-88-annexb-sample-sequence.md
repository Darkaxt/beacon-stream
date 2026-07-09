# Milestone 88: Annex B Sample Sequence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the beacon-test encoded-video path from a single diagnostic byte blob into a deterministic multi-sample Annex B stream that Android can queue with frame timestamps.

**Architecture:** Keep this as a diagnostic path, not GameStream transport. The server continues to expose one static H.264 Annex B asset, but the asset becomes a short multi-frame clip and the server test proves it contains multiple start-code-delimited NAL units. Android adds a focused Annex B access-unit splitter that groups NAL units into decoder samples and the HTTP sample provider emits those samples with monotonic presentation timestamps derived from the stream FPS.

**Tech Stack:** ASP.NET minimal APIs, copied H.264 diagnostic asset, Java Android JVM tests, Android MediaCodec sample provider boundary, xUnit API tests.

---

### Task 1: RED Server Multi-Frame Asset Test

**Files:**
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`

- [x] **Step 1: Strengthen the encoded-video asset test**

Update `BeaconTestEncodedVideoAssetReturnsAnnexBBytes` so it still asserts `200`, `video/H264`, non-empty bytes, and the `00 00 00 01` Annex B prefix, then add a count of 4-byte start codes and assert at least 4 start codes. Add this helper inside `ClientApiTests`:

```csharp
private static int CountAnnexBStartCodes(byte[] bytes)
{
    int count = 0;
    for (int index = 0; index <= bytes.Length - 4; index++)
    {
        if (bytes[index] == 0 && bytes[index + 1] == 0 && bytes[index + 2] == 0 && bytes[index + 3] == 1)
        {
            count++;
        }
    }

    return count;
}
```

- [x] **Step 2: Run RED server test**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter FullyQualifiedName~BeaconTestEncodedVideoAssetReturnsAnnexBBytes
```

Expected: fail because the existing one-frame asset does not expose enough 4-byte Annex B start codes for a sequence diagnostic.

Observed: failed with `Assert.True() Failure` on the new start-code count assertion.

### Task 2: GREEN Server Multi-Frame Diagnostic Asset

**Files:**
- Replace generated binary: `src/Beacon.Server/Assets/beacon-test-color-bars.h264`

- [x] **Step 1: Generate a deterministic short H.264 diagnostic clip**

Generate a 12-frame Annex B H.264 stream at `2560x1600@120` with keyframes only so every frame is independently delimited for simple diagnostic playback:

```powershell
ffmpeg -hide_banner -loglevel error -f lavfi -i smptebars=size=2560x1600:rate=120 -frames:v 12 -c:v libx264 -preset ultrafast -tune zerolatency -crf 51 -pix_fmt yuv420p -x264-params keyint=1:min-keyint=1:scenecut=0 -f h264 src\Beacon.Server\Assets\beacon-test-color-bars.h264
```

- [x] **Step 2: Verify the asset shape locally**

Run:

```powershell
$bytes = [IO.File]::ReadAllBytes('src\Beacon.Server\Assets\beacon-test-color-bars.h264')
$count = 0
for ($i = 0; $i -le $bytes.Length - 4; $i++) {
  if ($bytes[$i] -eq 0 -and $bytes[$i + 1] -eq 0 -and $bytes[$i + 2] -eq 0 -and $bytes[$i + 3] -eq 1) { $count++ }
}
"bytes=$($bytes.Length) startCodes=$count"
```

Expected: byte length is non-zero and `startCodes` is at least `4`.

Observed: `bytes=185327 startCodes=24`.

- [x] **Step 3: Run GREEN server test**

Run:

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter FullyQualifiedName~BeaconTestEncodedVideoAssetReturnsAnnexBBytes
```

Expected: pass.

Observed: focused `BeaconTestEncodedVideoAssetReturnsAnnexBBytes` passed.

### Task 3: RED Android Annex B Access-Unit Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/AnnexBAccessUnitSplitterTest.java`

- [x] **Step 1: Add access-unit splitter tests**

Create tests that prove:

```java
@Test
public void groupsParameterSetsWithFollowingSlice() {
    byte[] bytes = concat(
        start(), new byte[] { 0x67, 0x01 },
        start(), new byte[] { 0x68, 0x02 },
        start(), new byte[] { 0x65, 0x03 },
        start(), new byte[] { 0x65, 0x04 });

    List<byte[]> samples = AnnexBAccessUnitSplitter.split(bytes);

    assertEquals(2, samples.size());
    assertArrayEquals(concat(start(), new byte[] { 0x67, 0x01 }, start(), new byte[] { 0x68, 0x02 }, start(), new byte[] { 0x65, 0x03 }), samples.get(0));
    assertArrayEquals(concat(start(), new byte[] { 0x65, 0x04 }), samples.get(1));
}

@Test
public void rejectsBytesWithoutStartCode() {
    IllegalArgumentException exception = assertThrows(
        IllegalArgumentException.class,
        () -> AnnexBAccessUnitSplitter.split(new byte[] { 1, 2, 3 }));

    assertEquals("Encoded video bytes are not Annex B start-code delimited.", exception.getMessage());
}
```

Include private helpers:

```java
private static byte[] start() {
    return new byte[] { 0, 0, 0, 1 };
}

private static byte[] concat(byte[]... chunks) {
    int total = 0;
    for (byte[] chunk : chunks) {
        total += chunk.length;
    }

    byte[] result = new byte[total];
    int offset = 0;
    for (byte[] chunk : chunks) {
        System.arraycopy(chunk, 0, result, offset, chunk.length);
        offset += chunk.length;
    }

    return result;
}
```

- [x] **Step 2: Run RED splitter tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.AnnexBAccessUnitSplitterTest
```

Expected: compile failure because `AnnexBAccessUnitSplitter` does not exist.

Observed: failed at `compileDebugUnitTestJavaWithJavac` because `AnnexBAccessUnitSplitter` did not exist.

### Task 4: GREEN Android Annex B Access-Unit Splitter

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AnnexBAccessUnitSplitter.java`

- [x] **Step 1: Implement the splitter**

Add a package-private final class that:
- accepts only `00 00 00 01` start codes for the diagnostic path.
- keeps SPS/PPS/SEI/AUD-style non-VCL NAL units with the following VCL sample.
- starts a new sample for each VCL NAL after a previous VCL has been seen.
- returns immutable copies of sample byte ranges.
- throws `Encoded video bytes are not Annex B start-code delimited.` if no 4-byte start code exists.

- [x] **Step 2: Run GREEN splitter tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.AnnexBAccessUnitSplitterTest
```

Expected: pass.

Observed: focused `AnnexBAccessUnitSplitterTest` passed.

### Task 5: RED HTTP Provider Multi-Sample Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/HttpEncodedVideoSampleProviderFactoryTest.java`

- [x] **Step 1: Add provider sequence test**

Add a test using an Annex B byte array with SPS, PPS, and two IDR samples. Assert that:
- the provider fetches the resolved URL.
- first sample PTS is `0`.
- second sample PTS for `fps=120` is `8333`.
- third call returns EOS.
- sample byte arrays match the grouped access units.

- [x] **Step 2: Run RED provider tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.HttpEncodedVideoSampleProviderFactoryTest
```

Expected: fail because the provider still emits one full-blob sample with one timestamp.

Observed: `emitsAnnexBAccessUnitsWithFrameTimestamps` failed because the first sample still contained the full blob.

### Task 6: GREEN HTTP Provider Multi-Sample Queue

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/HttpEncodedVideoSampleProviderFactory.java`

- [x] **Step 1: Replace `SingleSampleProvider` with sequence provider**

After fetching bytes, call `AnnexBAccessUnitSplitter.split(bytes)`, then create one `EncodedVideoSample.data(...)` per access unit. Use `presentationTimeUs = index * (1_000_000L / plan.fps())`.

- [x] **Step 2: Run GREEN provider tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.HttpEncodedVideoSampleProviderFactoryTest --tests dev.beacon.android.AnnexBAccessUnitSplitterTest
```

Expected: pass.

Observed: focused `HttpEncodedVideoSampleProviderFactoryTest` and `AnnexBAccessUnitSplitterTest` passed.

### Task 7: Documentation, Emulator Smoke, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-88-annexb-sample-sequence.md`

- [x] **Step 1: Update README**

Document that the beacon-test encoded-video path is now a short multi-sample diagnostic sequence and Android queues frame samples with PTS values. Keep the explicit warning that this is still not full GameStream/Moonlight transport.

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

Observed: Gradle `test assembleDebug` passed. `dotnet test Beacon.slnx` passed with all .NET test projects green. The first .NET run exceeded the external shell command budget before returning, so it was rerun with a larger tool budget and passed.

- [x] **Step 4: Emulator smoke**

Run the server with:

```powershell
$env:ASPNETCORE_URLS='http://127.0.0.1:5000'
$env:BEACON_STREAMING_BACKEND='beacon-test'
$env:BEACON_TEST_STREAM_KIND='encoded-video'
dotnet run --project src\Beacon.Server
```

Install/start the debug APK on the emulator, launch the test stream, then verify:
- server `/admin/snapshot` stream state is `running`.
- APK status includes `Native encoded video stream started`.
- logcat has no `FATAL EXCEPTION`, `CodecException`, `Cleartext HTTP traffic`, `Launch failed`, or `Encoded video surface is not ready`.

Observed: server `/health` returned `{"status":"ok"}` on `127.0.0.1:5000`, the encoded asset route returned `200 video/H264` with `185327` bytes, the APK reported `Native encoded video stream started. codec=h264 container=annex-b video=/streams/beacon-test/color-bars.h264 2560x1600@120`, server `/admin/snapshot` reported the stream state as `running` with `error=null`, and logcat had no required failure-signature hits.

- [ ] **Step 5: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
