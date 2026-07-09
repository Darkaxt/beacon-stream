# Milestone 106: H.264 RTP Parameter Set Injection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Android GameStream H.264 RTP decoding consume descriptor-provided `sprop-parameter-sets` metadata by prepending decoded SPS/PPS Annex B NAL units before the first video-coded H.264 sample.

**Architecture:** Add a small `H264ParameterSets` value object that parses comma-separated Base64 NAL units from metadata aliases: `h264SpropParameterSets`, `spropParameterSets`, and `sprop-parameter-sets`. Extend `H264RtpSampleProvider` with an optional parameter-set constructor and inject the Annex B parameter sets only once, before the first sample that contains a VCL NAL unit. Wire `GameStreamRtpVideoSessionClient` to pass parsed parameter sets only for `codec=h264`; non-H.264 providers remain unchanged.

**Tech Stack:** Java 17, Android minSdk 26, JVM unit tests, RFC 6184 `sprop-parameter-sets` metadata shape, existing RTP packet/sample-provider boundary, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED H.264 Parameter Set Parser Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/H264ParameterSetsTest.java`
- Create later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/H264ParameterSets.java`

- [x] **Step 1: Add parser tests**

Create tests that assert:
- empty/null metadata returns an absent value with an empty Annex B payload;
- comma-separated Base64 SPS/PPS values decode into `00 00 00 01`-prefixed Annex B NAL units;
- surrounding whitespace is ignored;
- invalid Base64 fails with `H.264 sprop-parameter-sets metadata is invalid.`;
- decoded empty NAL units fail with the same diagnostic;
- decoded non-parameter-set NAL types fail with the same diagnostic.

- [x] **Step 2: Run RED parser tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.H264ParameterSetsTest
```

Expected: fail because `H264ParameterSets` does not exist.

### Task 2: GREEN H.264 Parameter Set Parser

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/H264ParameterSets.java`

- [x] **Step 1: Implement value object**

Create `final class H264ParameterSets` with:
- `static H264ParameterSets empty()`;
- `static H264ParameterSets fromSpropParameterSets(String value)`;
- `boolean present()`;
- `byte[] annexB()`.

Use `java.util.Base64.getDecoder()` and a four-byte Annex B start code. Accept only NAL types 7 and 8 for this milestone. Return defensive copies.

- [x] **Step 2: Run GREEN parser tests**

Run the focused `H264ParameterSetsTest` command again and confirm it passes.

### Task 3: RED H.264 Sample Provider Injection Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/H264RtpSampleProviderTest.java`
- Modify later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/H264RtpSampleProvider.java`

- [x] **Step 1: Add VCL injection test**

Create a provider with parameter sets and a single IDR RTP packet. Assert the first sample equals SPS + PPS + IDR in Annex B form.

- [x] **Step 2: Add one-shot injection test**

Create a provider with parameter sets and two VCL RTP packets. Assert the first sample is prefixed and the second sample is not.

- [x] **Step 3: Add non-VCL deferral test**

Create a provider with parameter sets, then an SPS packet, then an IDR packet. Assert the SPS-only sample is not prefixed by the configured parameter sets, and the later IDR sample is prefixed.

- [x] **Step 4: Run RED provider tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.H264RtpSampleProviderTest
```

Expected: fail because `H264RtpSampleProvider` does not accept parameter sets yet.

### Task 4: GREEN H.264 Sample Provider Injection

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/H264RtpSampleProvider.java`

- [x] **Step 1: Add optional constructor and state**

Keep the existing constructor and delegate it to `this(source, H264ParameterSets.empty())`. Add fields for `H264ParameterSets parameterSets` and `boolean parameterSetsInjected`.

- [x] **Step 2: Inject before first VCL sample**

Before `EncodedVideoSample.data(...)` is created, check whether configured parameter sets are present, not yet injected, and the Annex B sample contains a VCL NAL type 1 through 5. If true, prepend `parameterSets.annexB()` to the sample and mark injected. Use local Annex B start-code scanning; do not introduce timers, sleeps, retries, or network behavior.

- [x] **Step 3: Run GREEN provider tests**

Run the focused `H264RtpSampleProviderTest` command again and confirm it passes.

### Task 5: RED GameStream Session Wiring Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtpVideoSessionClientTest.java`
- Modify later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoSessionClient.java`

- [x] **Step 1: Add metadata wiring test**

Start `GameStreamRtpVideoSessionClient` with `codec=h264` and `h264SpropParameterSets=<base64-sps>,<base64-pps>`, feed one IDR RTP packet, and assert the consumer sample provider emits SPS + PPS + IDR.

- [x] **Step 2: Add metadata alias tests**

Add focused tests proving `spropParameterSets` and `sprop-parameter-sets` aliases also reach the H.264 provider.

- [x] **Step 3: Add invalid metadata diagnostic test**

Start with `codec=h264` and invalid parameter-set metadata. Assert start fails with a diagnostic containing `H.264 sprop-parameter-sets metadata is invalid.` and the RTP source closes once through the ordered source.

- [x] **Step 4: Run RED session tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtpVideoSessionClientTest
```

Expected: fail because `GameStreamRtpVideoSessionClient` still constructs `new H264RtpSampleProvider(source)` without metadata.

### Task 6: GREEN GameStream Session Wiring

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoSessionClient.java`

- [x] **Step 1: Parse metadata aliases**

Add a private helper that returns the first non-empty value from `h264SpropParameterSets`, `spropParameterSets`, and `sprop-parameter-sets`.

- [x] **Step 2: Pass parsed parameter sets into the H.264 provider**

For `codec=h264`, call `new H264RtpSampleProvider(source, H264ParameterSets.fromSpropParameterSets(metadataValue))`. Keep non-H.264 on `GameStreamRtpVideoSampleProvider`.

- [x] **Step 3: Keep close ownership on failure**

If provider creation fails because metadata is invalid, the existing `try` around consumer startup must close the ordered RTP source and return an unsupported result with the parser diagnostic.

- [x] **Step 4: Run GREEN session tests**

Run the focused `GameStreamRtpVideoSessionClientTest` command again and confirm it passes.

### Task 7: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-106-h264-parameter-sets.md`

- [x] **Step 1: Update docs**

Document that Milestone 106 accepts H.264 `sprop-parameter-sets` metadata from GameStream descriptors and injects SPS/PPS before the first VCL sample. State that this does not parse RTSP DESCRIBE SDP bodies yet, does not add retransmission, and does not change HEVC/AV1.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
$matches = git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("; if ($LASTEXITCODE -eq 1) { "NO_MATCHES"; exit 0 }; if ($LASTEXITCODE -eq 0) { $matches; exit 1 }; exit $LASTEXITCODE
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout patterns.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk
adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity
```

Expected: Android tests/APK build, .NET solution tests, and emulator launch smoke pass.

- [ ] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
