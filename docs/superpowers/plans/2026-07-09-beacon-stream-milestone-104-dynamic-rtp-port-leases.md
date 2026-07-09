# Milestone 104: Dynamic GameStream RTP Port Leases Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Android GameStream's static RTP client ports with dynamically leased UDP sockets that are advertised during RTSP SETUP and reused by the RTP video source.

**Architecture:** Add a small `GameStreamRtpPortLease` boundary that owns the audio, video, and control RTP UDP sockets for a GameStream session. The RTSP transport session client opens that lease before SETUP, `GameStreamRtspHandshakeClient` advertises the lease's actual local ports, and `GameStreamUdpRtpPacketSourceFactory` consumes the already-bound video socket from `GameStreamRtspSessionInfo` instead of rebinding the same port later. Direct handshake tests keep an explicit static test lease so the old protocol expectations remain deterministic.

**Tech Stack:** Java Android client boundaries, fakeable UDP socket factories, JVM unit tests, README/extraction-map documentation, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED RTP Port Lease Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtpPortLeaseTest.java`

- [x] **Step 1: Add dynamic bind and local-port capture test**

Create three recording `RtpDatagramSocket` instances with local ports `61000`, `61002`, and `61004`. Use a recording `RtpDatagramSocketFactory` whose `bind(int localPort)` records every requested port and returns those sockets. Assert `GameStreamRtpPortLease.open(factory)` calls `bind(0)` three times and exposes the three local ports.

- [x] **Step 2: Add close ownership test**

Use three recording sockets, call `lease.close()`, and assert each socket is closed exactly once. Call `lease.close()` again and assert the counts do not increase.

- [x] **Step 3: Add video socket transfer test**

Call `RtpDatagramSocket video = lease.takeVideoSocket()`, assert it returns the leased video socket, then call `lease.close()` and assert only audio/control are closed by the lease. Close the transferred video socket directly and assert its close count becomes one.

- [x] **Step 4: Add partial-open failure cleanup test**

Make the factory return audio, then throw `IOException("video busy")` while opening video. Assert `GameStreamRtpPortLease.open(factory)` throws `IllegalStateException` with message `RTP UDP port lease failed: video busy` and closes the already-opened audio socket once.

- [x] **Step 5: Run RED lease tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtpPortLeaseTest
```

Expected: fail because `GameStreamRtpPortLease` does not exist.

### Task 2: GREEN RTP Port Lease Boundary

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpPortLease.java`

- [x] **Step 1: Add `GameStreamRtpPortLease.open(...)`**

Create a final class with `open(RtpDatagramSocketFactory factory)` that binds audio, video, and control sockets with `factory.bind(0)`, validates non-null sockets, captures their `localPort()` values, and converts `IOException` into `IllegalStateException("RTP UDP port lease failed: " + safeMessage(ex), ex)`.

- [x] **Step 2: Add explicit static test lease factory**

Add `staticPorts(int audioClientPort, int videoClientPort, int controlClientPort)` for deterministic unit tests. It returns a lease without sockets and only exposes the supplied ports; `close()` is a no-op.

- [x] **Step 3: Add `takeVideoSocket()` and idempotent close**

Implement `takeVideoSocket()` so the video socket can be transferred exactly once. Implement `close()` to close audio/control and any untaken video socket once.

- [x] **Step 4: Run GREEN lease tests**

Run the focused `GameStreamRtpPortLeaseTest` command again and confirm it passes.

### Task 3: RED RTSP Dynamic Port Usage Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspHandshakeClientTest.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtspTransportSessionClientTest.java`

- [x] **Step 1: Update handshake tests to pass an explicit test lease**

Create helper `testPortLease()` returning `GameStreamRtpPortLease.staticPorts(50000, 50002, 50004)` and pass it to `GameStreamRtspHandshakeClient` so existing request assertions remain deterministic.

- [x] **Step 2: Add transport-session dynamic lease test**

In `GameStreamRtspTransportSessionClientTest`, inject a recording RTP lease factory returning `GameStreamRtpPortLease.staticPorts(61000, 61002, 61004)`. Start the session and assert the RTSP SETUP requests contain `X-GS-ClientPort=61000-61001`, `61002-61003`, and `61004-61005`, and result session info exposes those ports.

- [x] **Step 3: Add lease failure diagnostic test**

Inject an RTP lease factory that throws `IllegalStateException("RTP UDP port lease failed: video busy")`. Assert start returns failed diagnostic `RTP UDP port lease failed: video busy` and the RTSP transport lease is closed.

- [x] **Step 4: Run RED RTSP tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtspHandshakeClientTest --tests dev.beacon.android.GameStreamRtspTransportSessionClientTest
```

Expected: fail because the handshake and transport session client do not accept or use RTP port leases yet.

### Task 4: GREEN RTSP Lease Wiring

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpPortLeaseFactory.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspHandshakeClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspTransportSessionClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtspSessionInfo.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidNativeStreamClientFactory.java`

- [x] **Step 1: Add lease factory interface**

Create:

```java
package dev.beacon.android;

public interface GameStreamRtpPortLeaseFactory {
    GameStreamRtpPortLease open();
}
```

- [x] **Step 2: Inject lease into handshake**

Add a `GameStreamRtpPortLease` field to `GameStreamRtspHandshakeClient`. Existing constructors should delegate to a constructor using `GameStreamRtpPortLease.staticPorts(50000, 50002, 50004)` for deterministic direct tests. The new constructor accepts an explicit lease and fails with `GameStreamRtspSessionResult.failed("GameStream RTP port lease is missing.")` if the lease is null.

- [x] **Step 3: Advertise leased ports**

Replace `AudioClientPort`, `VideoClientPort`, and `ControlClientPort` constants in `startHandshake(...)` with `rtpPortLease.audioClientPort()`, `rtpPortLease.videoClientPort()`, and `rtpPortLease.controlClientPort()`.

- [x] **Step 4: Store lease in session info**

Extend `GameStreamRtspSessionInfo.startedWithClientPorts(...)` with an overload that accepts `GameStreamRtpPortLease rtpPortLease`, stores it, and exposes `rtpPortLease()`. Existing callers keep the old overload and get a null lease.

- [x] **Step 5: Open RTP lease in transport session client**

Add a `GameStreamRtpPortLeaseFactory` constructor dependency to `GameStreamRtspTransportSessionClient`. In `start(...)`, open the RTP lease after RTSP transport lease creation and before `GameStreamRtspHandshakeClient.start(...)`. On any failed start, close both leases. On success, retain both until `stop()`.

- [x] **Step 6: Wire Android default composition**

Update `AndroidNativeStreamClientFactory.socketRtspSessionClient()` to construct the transport session client with `GameStreamRtpPortLease::open` backed by `new JavaRtpDatagramSocketFactory()`.

- [x] **Step 7: Run GREEN RTSP tests**

Run the focused RTSP command again and confirm it passes.

### Task 5: RED UDP Video Source Lease Consumption Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamUdpRtpPacketSourceFactoryTest.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamNativeStreamClientTest.java`

- [x] **Step 1: Add video lease consumption test**

Create `GameStreamRtspSessionInfo` with a `GameStreamRtpPortLease` that owns a recording video socket. Assert `GameStreamUdpRtpPacketSourceFactory.create(...)` returns `RtpDatagramPacketSource` without calling the fallback socket factory and that closing the source closes the transferred video socket.

- [x] **Step 2: Keep fallback bind test**

Rename the existing static-port test to make clear it covers legacy session info without an attached lease and still binds the advertised video client port.

- [x] **Step 3: Add native cleanup regression**

In `GameStreamNativeStreamClientTest`, create started session info with an RTP lease and a video client that fails. Assert the RTSP session is stopped so the lease can be closed by the RTSP session client.

- [x] **Step 4: Run RED UDP/native tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamUdpRtpPacketSourceFactoryTest --tests dev.beacon.android.GameStreamNativeStreamClientTest
```

Expected: fail because `GameStreamUdpRtpPacketSourceFactory` still always rebinds by port.

### Task 6: GREEN UDP Video Source Lease Consumption

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamUdpRtpPacketSourceFactory.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtpDatagramPacketSource.java`

- [x] **Step 1: Prefer attached video lease socket**

In `GameStreamUdpRtpPacketSourceFactory.create(...)`, if `sessionInfo.rtpPortLease()` is non-null, call `takeVideoSocket()` and return a `RtpDatagramPacketSource` using that socket. If no lease exists, keep the existing fallback bind by `sessionInfo.videoClientPort()`.

- [x] **Step 2: Add transferred-socket diagnostics**

If `takeVideoSocket()` returns null or throws because the video socket has already been transferred, throw `IllegalStateException("GameStream RTP video socket lease is unavailable.")`.

- [x] **Step 3: Run GREEN UDP/native tests**

Run the focused UDP/native command again and confirm it passes.

### Task 7: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-104-dynamic-rtp-port-leases.md`

- [x] **Step 1: Update docs**

Document that Milestone 104 leases Android RTP UDP sockets dynamically, advertises those actual ports during RTSP SETUP, and reuses the leased video socket for RTP intake. State that jitter/reordering, retransmission, audio/control RTP processing, HEVC/AV1 depacketization, and parameter-set injection remain future work.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout/connect-timeout patterns.

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
