# Milestone 109: GameStream Replacement Cleanup And Loopback Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Android GameStream native-stream replacement starts clean up the previous video/RTSP session before attempting a new session, and add no-phone loopback coverage that proves the native route can carry a negotiated H.264 RTP sample through RTSP setup into the RTP video consumer.

**Architecture:** Keep `GameStreamNativeStreamClient` as the session owner. A new start must release any active video session before replacing the RTSP session, so a failed replacement cannot leave stale media running. Add JVM loopback coverage using fake RTSP transport and fake leased RTP datagram sockets; this exercises the production `GameStreamRtspTransportSessionClient`, `GameStreamUdpRtpPacketSourceFactory`, `GameStreamRtpVideoSessionClient`, H.264 access-unit provider, and native stream owner without a real phone, emulator-only instrumentation stack, sleeps, or timeouts.

**Tech Stack:** Java 17, Android JVM unit tests, existing GameStream RTSP/RTP fakeable boundaries, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED Replacement Cleanup Test

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamNativeStreamClientTest.java`
- Modify later: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamNativeStreamClient.java`

- [x] **Step 1: Add queued RTSP result helper**

Add a test helper that can return different RTSP results across repeated `start(...)` calls:

```java
private static final class QueuedRtspSessionClient implements GameStreamRtspSessionClient {
    private final List<GameStreamRtspSessionResult> results;
    private int startCount;
    private int stopCount;

    private QueuedRtspSessionClient(GameStreamRtspSessionResult... results) {
        this.results = Arrays.asList(results);
    }

    @Override
    public GameStreamRtspSessionResult start(GameStreamEndpointPlan plan) {
        return results.get(startCount++);
    }

    @Override
    public void stop() {
        stopCount++;
    }
}
```

- [x] **Step 2: Add failing replacement failure test**

Add:

```java
@Test
public void failedReplacementStartStopsPreviousVideoSession() {
    QueuedRtspSessionClient rtspClient = new QueuedRtspSessionClient(
        startedRtspSessionResult(),
        GameStreamRtspSessionResult.failed("RTSP replacement failed."));
    RecordingVideoSessionClient videoClient = new RecordingVideoSessionClient(
        NativeStreamStartResult.started("Native GameStream video session started."));
    GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(rtspClient, videoClient);

    NativeStreamStartResult first = client.start(
        completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session", completeRtpMetadata()));
    NativeStreamStartResult second = client.start(
        completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session", completeRtpMetadata()));

    assertTrue(first.success());
    assertFalse(second.success());
    assertEquals("RTSP replacement failed.", second.diagnostic());
    assertEquals(1, videoClient.stopCount);
    assertEquals(1, rtspClient.stopCount);
}
```

- [x] **Step 3: Run RED replacement test**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamNativeStreamClientTest
```

Expected: fail because `GameStreamNativeStreamClient.start(...)` does not stop an already active video/RTSP session before a replacement RTSP start fails.

### Task 2: GREEN Replacement Cleanup

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamNativeStreamClient.java`

- [x] **Step 1: Stop active session before replacement start**

At the start of `start(...)`, before validating the new endpoint map, call a helper that stops the active video session and RTSP session only when either is currently active:

```java
private void stopActiveSessionBeforeReplacement() {
    if (videoSessionActive) {
        videoSessionActive = false;
        stopVideoSessionQuietly();
    }

    stopRtspSession();
}
```

Then call `stopActiveSessionBeforeReplacement();` at the beginning of `start(...)`.

- [x] **Step 2: Run GREEN replacement tests**

Run the focused `GameStreamNativeStreamClientTest` command again and confirm it passes.

### Task 3: RED Loopback RTP Session Coverage

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamLoopbackSessionTest.java`

- [x] **Step 1: Add loopback test skeleton**

Create a test that composes the real native stream owner, transport session client, UDP RTP source factory, and RTP video session client:

```java
@Test
public void loopbackRtspSetupFeedsH264RtpSampleProvider() {
    LoopbackDatagramSocket audio = new LoopbackDatagramSocket(61000);
    LoopbackDatagramSocket video = new LoopbackDatagramSocket(
        61002,
        rtpPacket(1, 90000L, false, new byte[] {0x41, 0x11}),
        rtpPacket(2, 90000L, true, new byte[] {0x41, 0x22}));
    LoopbackDatagramSocket control = new LoopbackDatagramSocket(61004);
    RecordingRtpVideoConsumer consumer = new RecordingRtpVideoConsumer();
    GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(
        new GameStreamRtspTransportSessionClient(
            new LoopbackRtspLeaseFactory(),
            () -> GameStreamRtpPortLease.open(new LoopbackDatagramSocketFactory(audio, video, control)),
            new FixedSdpPayloadProvider("v=0\r\ns=Beacon Loopback\r\n")),
        new GameStreamRtpVideoSessionClient(
            new GameStreamUdpRtpPacketSourceFactory(),
            consumer));

    NativeStreamStartResult result = client.start(completeGameStreamConnection());
    EncodedVideoSample sample = consumer.sampleProvider.nextSample();
    client.stop();

    assertTrue(result.success());
    assertArrayEquals(
        concat(start(), new byte[] {0x41, 0x11}, start(), new byte[] {0x41, 0x22}),
        sample.data());
    assertEquals(1, consumer.stopCount);
    assertEquals(1, audio.closeCount);
    assertEquals(1, video.closeCount);
    assertEquals(1, control.closeCount);
}
```

Include local fake helpers for `LoopbackRtspLeaseFactory`, `LoopbackDatagramSocketFactory`, `LoopbackDatagramSocket`, `RecordingRtpVideoConsumer`, packet construction, start-code concatenation, a complete GameStream descriptor with H.264 metadata, and `FixedSdpPayloadProvider`.

- [x] **Step 2: Run RED loopback test**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamLoopbackSessionTest
```

Expected: compile failure until the helper file is complete, then pass once the loopback fixture exercises the existing production path. If the completed test passes immediately, do not change production only for ceremony; the value of this task is the no-phone integration coverage.

### Task 4: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-109-gamestream-replacement-cleanup.md`

- [x] **Step 1: Update docs**

Document that Milestone 109 stops active GameStream video/RTSP state before a replacement start and adds JVM loopback coverage for RTSP setup into H.264 RTP sample consumption. State that this still does not add a full Android instrumentation suite, real encoder integration, jitter timing, retransmission, HEVC/AV1 depacketization, audio/control RTP processing, controller, or native touch/gesture protocol support.

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
