# Milestone 100: Android UDP RTP Source Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a real but fakeable Android UDP datagram boundary that can feed RTP packets from the negotiated GameStream video client port.

**Architecture:** Introduce a narrow `RtpDatagramSocket` abstraction and a `JavaRtpDatagramSocketFactory` backed by `java.net.DatagramSocket`. `RtpDatagramPacketSource` implements `RtpPacketSource` by receiving one UDP datagram and parsing it as a complete RTP packet. `GameStreamUdpRtpPacketSourceFactory` binds the negotiated `GameStreamRtspSessionInfo.videoClientPort()` and returns that source. This milestone still does not implement GameStream depacketization, MediaCodec consumption, dynamic port leasing, audio/control RTP, or default APK GameStream video wiring.

**Tech Stack:** Java Android networking boundary, JVM unit tests with fake sockets, README/extraction-map documentation, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED Datagram Source Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtpDatagramPacketSourceTest.java`

- [x] **Step 1: Add receives RTP packet test**

Use a fake datagram socket that copies a complete RTP packet into the provided buffer and returns its byte count. Assert `RtpDatagramPacketSource.nextPacket()` returns the parsed sequence number, timestamp, SSRC, and payload.

- [x] **Step 2: Add receive failure diagnostic test**

Use a fake datagram socket whose receive throws `IOException("socket closed")`. Assert `nextPacket()` throws `IllegalStateException("RTP datagram receive failed: socket closed")`.

- [x] **Step 3: Add close delegates once test**

Call `close()` twice and assert the fake socket is closed once.

- [x] **Step 4: Run RED datagram source tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.RtpDatagramPacketSourceTest
```

Expected: fail because `RtpDatagramSocket` and `RtpDatagramPacketSource` do not exist.

Result: the focused Gradle run failed at `:app:compileDebugUnitTestJavaWithJavac` because `RtpDatagramSocket` and `RtpDatagramPacketSource` did not exist.

### Task 2: GREEN Datagram Source

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtpDatagramSocket.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtpDatagramPacketSource.java`

- [x] **Step 1: Add `RtpDatagramSocket`**

Create an interface with `int receive(byte[] buffer) throws IOException`, `int localPort()`, and `void close()`.

- [x] **Step 2: Add `RtpDatagramPacketSource`**

Implement `RtpPacketSource`. Allocate a reusable datagram buffer, call `socket.receive(buffer)`, copy exactly the returned byte count, and parse it with `RtpPacket.parse(...)`. Convert `IOException` to `IllegalStateException("RTP datagram receive failed: " + message)`. Make `close()` idempotent.

- [x] **Step 3: Run GREEN datagram source tests**

Run the focused datagram source tests and confirm they pass.

Result: the focused `RtpDatagramPacketSourceTest` run passed.

### Task 3: RED UDP Factory Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamUdpRtpPacketSourceFactoryTest.java`

- [x] **Step 1: Add binds negotiated video client port test**

Use a fake datagram socket factory. Pass a started session info with `videoClientPort=50002`. Assert the factory binds `50002` and returns an `RtpDatagramPacketSource`.

- [x] **Step 2: Add missing video client port diagnostic test**

Pass legacy session info with no client ports. Assert `create(...)` throws `IllegalStateException("GameStream RTP video client port is unavailable.")`.

- [x] **Step 3: Add bind failure diagnostic test**

Make the socket factory throw `IOException("address in use")`. Assert `create(...)` throws `IllegalStateException("RTP UDP socket open failed: address in use")`.

- [x] **Step 4: Run RED UDP factory tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamUdpRtpPacketSourceFactoryTest
```

Expected: fail because `RtpDatagramSocketFactory`, `JavaRtpDatagramSocketFactory`, and `GameStreamUdpRtpPacketSourceFactory` do not exist.

Result: the focused Gradle run failed at `:app:compileDebugUnitTestJavaWithJavac` because `RtpDatagramSocketFactory` and `GameStreamUdpRtpPacketSourceFactory` did not exist.

### Task 4: GREEN UDP Factory

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtpDatagramSocketFactory.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/JavaRtpDatagramSocketFactory.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamUdpRtpPacketSourceFactory.java`

- [x] **Step 1: Add socket factory interface**

Create `RtpDatagramSocketFactory.bind(int localPort) throws IOException`.

- [x] **Step 2: Add Java datagram socket adapter**

Implement `JavaRtpDatagramSocketFactory` with `new DatagramSocket(localPort)`. The adapter's `receive(byte[] buffer)` should use `DatagramPacket` and return `packet.getLength()`. Do not configure socket timeouts.

- [x] **Step 3: Add GameStream UDP RTP source factory**

Implement `GameStreamRtpPacketSourceFactory`. Require `sessionInfo.present()` and `sessionInfo.videoClientPort() > 0`; bind that port through the injected factory; wrap it in `RtpDatagramPacketSource`; convert bind `IOException` to `IllegalStateException("RTP UDP socket open failed: " + message)`.

- [x] **Step 4: Run GREEN UDP factory tests**

Run the focused UDP factory tests and confirm they pass.

Result: the focused `GameStreamUdpRtpPacketSourceFactoryTest` run passed.

### Task 5: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-100-android-udp-rtp-source.md`

- [x] **Step 1: Update docs**

Document that Milestone 100 adds a real/fakeable UDP datagram-to-RTP packet source, still not default-wired to MediaCodec decode or GameStream depacketization.

- [x] **Step 2: Static checks**

Run:

```powershell
git diff --check
git diff -U0 -- src tests | rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout|setSoTimeout|connect\\([^,]+,\\s*[0-9]+\\)|sleep\\("
```

Expected: no whitespace errors and no new sleep/timeout/socket-timeout/connect-timeout patterns.

Result: `git diff --check` passed, the working-tree diff scan returned `NO_MATCHES`, and the final staged `git diff --cached --check` plus staged diff scan also passed with `NO_MATCHES`.

- [x] **Step 3: Dynamic checks**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx
adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk
adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity
```

Expected: Android tests/APK build, .NET solution tests, and emulator launch smoke pass.

Result: `gradle --no-daemon -p src\Beacon.Android test assembleDebug` passed, `dotnet test Beacon.slnx` passed with 296 total .NET tests, `adb -s emulator-5554 install -r src\Beacon.Android\app\build\outputs\apk\debug\app-debug.apk` returned `Success`, and `adb -s emulator-5554 shell am start -W -n dev.beacon.android/.BeaconActivity` returned `Status: ok` with `LaunchState: COLD`.

- [ ] **Step 4: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.
