# Beacon Network And Hardware Benchmark Validation

Date: 2026-07-14

## Scope

This validation closes Gate 4 Task 12 by proving the existing deterministic benchmark stack and
adding the missing production-process transport acceptance. The acceptance launches the real
Beacon StreamWorker, controls it over Worker IPC, authenticates a Beacon session over MsQuic,
and collects benchmark traffic with the same portable `BenchmarkCollector` compiled into APK
StreamCore.

No compatibility transport or external streaming product participates in this path.

## Production Process Proof

The first failing acceptance invoked the native probe with `--benchmark-worker`. The probe did
not recognize that mode and exited with code 64, proving that the process-level benchmark path
was not covered.

The implemented probe now:

- sends the exact server-prepared benchmark plan through Worker IPC and the authenticated
  session stream;
- consumes all framed session replies, including coalesced reliable benchmark chunks;
- passes reliable observations and benchmark datagrams through APK StreamCore's production
  `BenchmarkCollector`;
- requires measured throughput, all loopback datagrams, and nonzero MsQuic RTT observations;
- observes the Worker authentication and disconnect events; and
- shuts the Worker down explicitly after the client disconnects.

All waits are event-driven. The process probe adds no lifecycle or cancellation timeout.

`scripts/test-stream-worker-integration.ps1` passed with:

```text
BEACON_WORKER_IPC_QUIC_OK AUTH INPUT FEEDBACK REAL_H264_ACCESS_UNIT DISCONNECT SHUTDOWN
BEACON_WORKER_BENCHMARK_OK AUTH RELIABLE DATAGRAM RTT DISCONNECT SHUTDOWN
BEACON_WORKER_STARTUP_EXIT 64
```

## Requirement Coverage

- Reliable throughput, datagram loss/reorder/RTT/jitter, and cancellation are covered by the
  Worker source and StreamCore collector tests plus the real process loopback.
- Decoder vectors, decode latency, presentation latency/drops, and power/thermal sampling are
  covered by Android JVM tests and server benchmark-suite tests.
- Automatic material fingerprint changes, unchanged-fingerprint reuse, manual always-new runs,
  preflight runs, stale completion rejection, and server-owned planning are covered by Core,
  Server, Android, Client Lab, and Playwright tests.
- `BenchmarkSessionPlannerTests.ValidatedBenchmarkConstrainsStreamWithoutChangingDisplayIntent`
  proves that insufficient 120 FPS evidence can select 60 FPS while retaining the Z Fold 7
  `2560x1600@120` display intent.
- No live bitrate adaptation was introduced. Completed raw evidence is validated and fed into
  the server planner.

## Validation Matrix

- `dotnet format Beacon.slnx --verify-no-changes --no-restore`: passed.
- `dotnet build Beacon.slnx -warnaserror --no-restore`: passed with zero warnings.
- `dotnet test Beacon.slnx --no-restore`: 509 passed.
- Windows native build and CTest: 23/23 passed.
- Production Worker video and benchmark process probes: passed.
- Android `clean,test,assembleDebug,assembleRelease,assembleDebugAndroidTest`: passed; 118
  Gradle tasks, 298 JVM tests, and x86_64/arm64 native StreamCore builds completed.
- Client Lab lint and 16 tests: passed.
- Client Lab Playwright lint and lifecycle test: passed.
- `FakeEndpointLiveTests`: 2 passed.
- Gate 3 architecture absence, secret fixtures, and Kestrel parser fixtures: passed.
- `git diff --check`: passed.

## Deferred Acceptance

Android native execution, instrumentation, and the real emulator benchmark remain pending because
another task currently owns the shared ADB/emulator state. This validation issued no ADB command.
It also made no display-topology or virtual-display change and did not access or control any
external streaming installation.
