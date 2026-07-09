# Milestone 105: RTP Sequence Reorder Source Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a bounded RTP sequence reordering source so Android GameStream video packets reach the sample provider in sequence order when UDP datagrams arrive slightly out of order.

**Architecture:** Add `RtpReorderingPacketSource` as a small wrapper around `RtpPacketSource`. It buffers out-of-order packets by 16-bit RTP sequence number, emits the next expected packet when available, drops duplicate/late packets, handles 16-bit wraparound, and uses a packet-count window to recover from missing packets without timeouts. `GameStreamRtpVideoSessionClient` wraps the UDP source before selecting the H.264 or raw sample provider.

**Tech Stack:** Java Android client boundaries, fakeable RTP packet sources, JVM unit tests, RFC 3550 RTP sequence-number behavior, README/extraction-map documentation, Gradle Android tests/build, .NET solution tests, emulator install/launch smoke.

---

### Task 1: RED RTP Reordering Source Tests

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/RtpReorderingPacketSourceTest.java`

- [x] **Step 1: Add in-order passthrough test**

Create a source with sequence numbers `10`, `11`, and `12`. Wrap it in `new RtpReorderingPacketSource(source, 4)` and assert `nextPacket()` returns `10`, `11`, `12`, then `null`.

- [x] **Step 2: Add out-of-order recovery test**

Create a source with sequence numbers `10`, `12`, `11`, and `13`. Assert the wrapper emits `10`, then buffers `12`, emits `11`, then emits buffered `12`, then `13`.

- [x] **Step 3: Add duplicate and late packet drop test**

Create a source with sequence numbers `10`, `11`, `11`, `10`, and `12`. Assert the wrapper emits `10`, `11`, `12`, then `null`.

- [x] **Step 4: Add wraparound ordering test**

Create a source with sequence numbers `65534`, `0`, `65535`, and `1`. Assert the wrapper emits `65534`, `65535`, `0`, and `1`.

- [x] **Step 5: Add bounded missing-packet recovery test**

Create a source with sequence numbers `10`, `12`, `13`, and `14`, with reorder window `2`. Assert the wrapper emits `10`, then advances past missing `11` when the window is full and emits `12`, `13`, and `14`. This proves the implementation does not wait forever for a lost packet and does not use a timeout.

- [x] **Step 6: Add close delegation test**

Call `close()` twice on the wrapper and assert the inner source is closed exactly once.

- [x] **Step 7: Run RED reorder tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.RtpReorderingPacketSourceTest
```

Expected: fail because `RtpReorderingPacketSource` does not exist.

### Task 2: GREEN RTP Reordering Source

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/RtpReorderingPacketSource.java`

- [x] **Step 1: Add constructor and state**

Create a final class implementing `RtpPacketSource` with constructor `RtpReorderingPacketSource(RtpPacketSource inner, int maxBufferedPackets)`. Reject null `inner` and `maxBufferedPackets < 1`.

- [x] **Step 2: Implement sequence arithmetic**

Add private helpers:

```java
private static int nextSequence(int sequenceNumber) {
    return (sequenceNumber + 1) & 0xFFFF;
}

private static int sequenceDistance(int fromInclusive, int toExclusive) {
    return (toExclusive - fromInclusive) & 0xFFFF;
}

private static boolean isLateOrDuplicate(int sequenceNumber, int expectedSequence) {
    int distance = sequenceDistance(expectedSequence, sequenceNumber);
    return distance >= 0x8000;
}
```

- [x] **Step 3: Implement ordered emission**

On the first packet, emit it immediately and set `expectedSequence` to the following sequence. For later packets, emit immediately when sequence equals expected, then drain any buffered contiguous packets. Buffer future packets in a `TreeMap<Integer, RtpPacket>` keyed by sequence distance from `expectedSequence` or an equivalent deterministic structure.

- [x] **Step 4: Drop duplicates and late packets**

If a packet's sequence is already buffered or is before the current expected sequence by RTP wrap-aware arithmetic, ignore it and keep reading.

- [x] **Step 5: Recover from missing packets by bounded count**

If no contiguous packet is available and the buffer size reaches `maxBufferedPackets`, choose the buffered packet with the smallest forward sequence distance from `expectedSequence`, emit it, and set `expectedSequence` to that packet's successor. Do not use sleeps, timers, socket timeouts, or cancellation timeouts.

- [x] **Step 6: Flush buffered packets at end of source**

When the inner source returns `null`, emit remaining buffered packets in forward sequence order before returning `null`.

- [x] **Step 7: Run GREEN reorder tests**

Run the focused `RtpReorderingPacketSourceTest` command again and confirm it passes.

### Task 3: RED GameStream Video Wiring Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamRtpVideoSessionClientTest.java`

- [x] **Step 1: Add source wrapper selection test**

Start `GameStreamRtpVideoSessionClient` with a non-H.264 plan and a recording source. Assert the consumer receives a `GameStreamRtpVideoSampleProvider`, then call `consumer.sampleProvider.nextSample()` with source packets arriving as sequence `10`, `12`, `11`. Assert the emitted sample data is sequence `10` payload first, then sequence `11`, then sequence `12`.

- [x] **Step 2: Add H.264 path wrapper test**

Start with `codec=h264` and FU-A fragments arriving as sequence `1`, `3`, `2`: FU-A start, FU-A end, FU-A middle. Assert `H264RtpSampleProvider.nextSample()` returns the reassembled Annex B sample in correct order instead of failing on end-before-middle.

- [x] **Step 3: Run RED wiring tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.GameStreamRtpVideoSessionClientTest
```

Expected: fail because `GameStreamRtpVideoSessionClient` still passes the raw packet source directly to the sample provider.

### Task 4: GREEN GameStream Video Wiring

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamRtpVideoSessionClient.java`

- [x] **Step 1: Wrap source before provider selection**

Create a private method:

```java
private static RtpPacketSource orderedSource(RtpPacketSource source) {
    return new RtpReorderingPacketSource(source, 16);
}
```

Pass `orderedSource(source)` into `sampleProvider(...)`.

- [x] **Step 2: Retain close ownership**

Keep `activeSource` as the ordered wrapper so `stop()` closes the wrapper, which closes the original source exactly once.

- [x] **Step 3: Run GREEN wiring tests**

Run the focused `GameStreamRtpVideoSessionClientTest` command again and confirm it passes.

### Task 5: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-09-beacon-stream-milestone-105-rtp-sequence-reorder.md`

- [x] **Step 1: Update docs**

Document that Milestone 105 adds bounded RTP sequence reordering before sample/depacketization. State clearly that this is not retransmission, does not guarantee loss recovery, and does not use timeouts. Jitter timing, retransmission, audio/control RTP processing, HEVC/AV1 depacketization, and parameter-set injection remain future work.

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
