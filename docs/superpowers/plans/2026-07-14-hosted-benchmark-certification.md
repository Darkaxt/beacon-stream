# Hosted Benchmark Certification Implementation Plan

> **Execution rule:** Complete each task test-first, validate the focused boundary, commit and
> push the validated slice, then continue. After dynamic acceptance, perform a refactor audit and
> rerun the complete validation set before the final sync.

**Goal:** Certify Beacon's manual and session-preflight benchmark workflows through production
Server policy, typed Worker IPC, production Worker benchmark/QUIC primitives, and the APK's sole
production StreamCore on a GitHub-hosted Android emulator.

**Architecture:** Extract a portable subset of the existing Beacon Worker without changing its
protocol or ownership. A test-only Linux child composes that subset and is owned by TestHost over
stdin/stdout Worker IPC. TestHost uses the existing Worker-backed benchmark runtime and ticket
authorizer while retaining fake host side effects and fake non-benchmark streaming.

**Design:** `docs/superpowers/specs/2026-07-14-hosted-benchmark-certification-design.md`

---

### Task 1: Establish A Portable Beacon Worker Core

**Files:**
- Create: `src/Beacon.StreamWorker/include/beacon/worker/video/worker_video_capabilities.h`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/video/production_video_capabilities.h`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/worker_host.h`
- Modify: `src/Beacon.StreamWorker/CMakeLists.txt`
- Modify: `native/CMakeLists.txt`
- Modify: `native/CMakePresets.json`
- Test: `tests/Beacon.Core.Tests/Architecture/ArchitectureRecoveryBoundaryTests.cs`
- Test: `tests/Beacon.StreamWorker.Tests/worker_host_tests.cpp`

- [x] Add failing architecture tests proving `worker_host.h` no longer includes capture/NVENC
  capability headers and that the portable target excludes every Windows capture/encode/pipe
  source.
- [x] Move only the capability value type and enum into the neutral header. Keep hardware probes
  and failure translation in the Windows production header/implementation.
- [x] Split CMake ownership into `BeaconStreamWorkerPortableCore` and the Windows-only
  `BeaconStreamWorkerCore`, preserving the existing public alias and executable output.
- [x] Add a Linux x64 native preset and select Schannel only on Windows, quictls elsewhere.
- [x] Run focused architecture tests and the full Windows native suite.
- [x] Commit and push the validated extraction.

### Task 2: Make The Production QUIC Listener Portable

**Files:**
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/quic_listener.h`
- Modify: `src/Beacon.StreamWorker/src/quic_listener.cpp`
- Modify: `src/Beacon.StreamWorker/src/main.cpp`
- Test: `tests/Beacon.StreamWorker.Tests/quic_listener_probe.cpp`
- Test: `tests/Beacon.Core.Tests/Architecture/ArchitectureRecoveryBoundaryTests.cs`

- [x] Add failing shape tests for a filesystem-path identity contract, platform-guarded Windows
  certificate imports, non-Windows PKCS#12 credentials, bounded identity bytes, and secure clear.
- [x] Change the listener identity parameter to `std::filesystem::path` without changing the
  Windows command line or identity file format.
- [x] Retain Schannel context import/key cleanup under `_WIN32`; use MsQuic's in-memory PKCS#12
  credential on quictls hosts.
- [x] Keep identity bytes alive through configuration lifetime and securely clear them on release.
- [x] Run the listener probe, native suite, warning-as-error build, and protected-route scan.
- [x] Commit and push the portable-listener slice.

### Task 3: Add The Typed Hosted Benchmark Worker

**Files:**
- Create: `tests/Beacon.StreamProtocol.Tests/hosted_benchmark_worker_channel.h`
- Create: `tests/Beacon.StreamProtocol.Tests/hosted_benchmark_worker_channel.cpp`
- Create: `tests/Beacon.StreamProtocol.Tests/hosted_benchmark_worker_channel_tests.cpp`
- Create: `tests/Beacon.StreamProtocol.Tests/hosted_benchmark_worker.cpp`
- Modify: `tests/Beacon.StreamProtocol.Tests/CMakeLists.txt`

- [x] Write failing native tests for exact big-endian frame reads/writes, malformed/oversized frame
  rejection, clean EOF, and terminal Worker completion.
- [x] Implement binary stdin/stdout framing using the existing maximum Worker message size and
  generated protobuf contract.
- [x] Compose `WorkerHost`, `AuthorizedQuicTicketStore`, `QuicListener`, outbound queue, and a
  no-video pipeline. Report video unavailable truthfully while keeping benchmark support active.
- [x] Publish hello/capabilities/ready, serialize all output through one queue, and shut down only
  on typed command, channel closure, or unrecoverable process failure.
- [x] Add fixed, non-secret process markers for readiness and clean shutdown on stderr.
- [x] Build/test the target on Linux and prove it is not part of Windows/Android product artifacts.
- [x] Commit and push the hosted Worker slice.

### Task 4: Let TestHost Own The Hosted Worker Process

**Files:**
- Create: `tests/Beacon.Server.TestHost/HostedBenchmarkWorkerOptions.cs`
- Create: `tests/Beacon.Server.TestHost/HostedBenchmarkWorkerProcessHost.cs`
- Create: `tests/Beacon.Server.TestHost/ReadWriteDuplexStream.cs`
- Modify: `tests/Beacon.Server.TestHost/BeaconTestRuntimeServices.cs`
- Modify: `tests/Beacon.Server.TestHost/Program.cs`
- Test: `tests/Beacon.Server.Tests/HostedBenchmarkWorkerOptionsTests.cs`
- Test: `tests/Beacon.Server.Tests/HostedBenchmarkWorkerProcessHostTests.cs`
- Test: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`

- [ ] Write failing tests for missing/executable/identity options, duplex stream directionality,
  handshake, command correlation, event publication, process generation, child exit, graceful
  shutdown, and disposal after a broken control channel.
- [ ] Implement a lazy process owner with redirected binary stdin/stdout and bounded stderr
  diagnostics. Reuse `StreamWorkerNamedPipeClient` for framing and protocol validation.
- [ ] Configure TestHost so the hosted path replaces only fake benchmark runtime and ticket
  authorization. Keep `FakeStreamingBackend` for non-benchmark sessions.
- [ ] Register `StreamWorkerStreamingBackend` as `IBenchmarkRuntime` and
  `StreamWorkerSessionAuthorizer` as `IStreamSessionAuthorizer`.
- [ ] Add process-level integration tests using a deterministic fake Worker child without sleeps,
  polling, fixed ports, or product timeouts.
- [ ] Run focused managed tests, format, warning-as-error build, and the complete managed suite.
- [ ] Commit and push the TestHost ownership slice.

### Task 5: Build Hosted Emulator Benchmark Acceptance

**Files:**
- Create: `scripts/build-native-linux.sh`
- Create: `scripts/test-hosted-emulator-benchmarks.sh`
- Create: `tests/scripts/test_hosted_emulator_benchmarks.py`
- Modify: `.github/workflows/ci.yml`
- Modify: `src/Beacon.Android/app/src/androidTest/java/dev/beacon/android/BeaconStreamCoreInstrumentationTest.java`
- Modify: `docs/validation/2026-07-14-hosted-emulator-stream.md`

- [ ] Write a fake-process/fake-ADB script test that proves structured Kestrel readiness parsing,
  exact instrumentation dispatch, required markers, snapshot validation, secret absence, child
  cleanup, and failure propagation.
- [ ] Add explicit instrumentation markers proving native network completion, expected
  real-hardware capability rejection, certified manual completion, and preflight completion.
- [ ] Build the Linux Worker through pinned dependencies and protoc, then start an isolated TestHost
  with the same PKCS#12 identity and an OS-assigned HTTPS port.
- [ ] Run the three Gate 4 methods explicitly against `10.0.2.2`, capture instrumentation/logcat,
  and verify Server snapshots after each transaction.
- [ ] Add the runner to the hosted Android job without touching local ADB.
- [ ] Run script unit tests, Android unit/build checks, static scans, and push for hosted execution.
- [ ] Inspect the GitHub run and record exact run id, commit, benchmark markers, Worker markers, and
  Server snapshot evidence in the validation document.
- [ ] Commit and push the dynamic evidence.

### Task 6: Refactor And Revalidate The Gate

**Files:**
- Modify only files justified by the evidence-driven audit.
- Modify: `docs/superpowers/plans/2026-07-10-beacon-stream-gates-3-5.md`
- Modify: `docs/validation/2026-07-14-hosted-emulator-stream.md`

- [ ] Audit process ownership, identity lifetime, zeroization, event-channel backpressure, request
  correlation, stop/revoke ordering, duplicate code, packaging boundaries, and diagnostics.
- [ ] Refactor only evidence-backed duplication or lifecycle defects and add regression tests first.
- [ ] Run `dotnet format`, warning-as-error build, all managed tests, Windows native tests, Linux
  hosted Worker tests, Android unit/build tests, Client Lab tests, protected-route scan, artifact
  inspection, and script simulations.
- [ ] Push the refactor and require all GitHub jobs, including hosted emulator benchmarks and the
  existing moving-frame acceptance, to pass again.
- [ ] Update the parent gate plan only with evidence that is directly certified.
- [ ] Commit and push the final Gate 4 certification record.
