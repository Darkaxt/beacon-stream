# Beacon Stream Gates 3–5 Implementation Plan

> **Execution rule:** implement each task test-first, run its focused validation, commit and
> push the slice, then continue. At every gate: implement, validate static/dynamic, sync,
> refactor from evidence, validate static/dynamic again, and sync again.

**Goal:** deliver one Beacon-owned StreamWorker/StreamCore protocol, benchmark it without a
phone, and prove a real H.264 SDR stream from the planned Windows virtual display to the
Android emulator without Apollo, Sunshine, or a compatibility route.

**Architecture:** .NET Beacon Service remains policy/control authority. A persistent native
C++20 StreamWorker runs in the interactive Windows session and communicates with Service by
typed named-pipe IPC. Worker and APK StreamCore use one authenticated MsQuic connection with
reliable control/input/feedback streams and unreliable congestion-controlled media
datagrams. Android uses native transport/frame assembly and the existing Java MediaCodec
Surface boundary.

**Authoritative source decisions:**
`docs/source-audits/2026-07-10-beacon-streamworker-streamcore.md`

**Hard constraints:**

- no Apollo/Sunshine/GameStream/Moonlight/RTSP/RTP/WebRTC/HTTP-media compatibility;
- no backend/transport selector or client-owned stream/display policy;
- no descriptor files, polling readiness, startup sleeps, task-cancellation timeouts, or
  lifecycle watchdogs;
- no HEVC, AV1, audio, HDR, FEC, or physical-phone optimization before the H.264 emulator
  gate;
- no product fallback to the physical display, another capture API, or software encoding;
- all dependency revisions are pinned and no submodules or symlinks are introduced.

## Gate 3: Worker, Protocol, And Fake Data Plane

### Task 1: Guard The Selected Architecture

**Files:**

- Modify: `tests/Beacon.Core.Tests/Architecture/ArchitectureRecoveryBoundaryTests.cs`
- Modify: `tests/Beacon.Core.Tests/Docs/ReadmeLinkTests.cs`
- Modify: `docs/extraction-map.md`

- [x] Add a failing architecture test that permits `MsQuic`, `NVENC`, `WGC`, and
  `MediaCodec` only behind the selected StreamWorker/StreamCore boundaries and continues to
  prohibit every compatibility name in runtime/test code.
- [x] Add a failing documentation test requiring the Gate 3 audit and this plan.
- [x] Update the extraction map only after the tests fail for the expected missing links.
- [x] Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter "ArchitectureRecoveryBoundaryTests|ReadmeLinkTests"
```

- [x] Commit: `test: guard Beacon native streaming boundaries`

### Task 2: Establish Reproducible Native Builds And Prove MsQuic Platforms

**Files:**

- Create: `native/CMakeLists.txt`
- Create: `native/CMakePresets.json`
- Create: `native/dependencies.lock.json`
- Create: `scripts/bootstrap-native-dependencies.ps1`
- Create: `src/Beacon.StreamProtocol/CMakeLists.txt`
- Create: `src/Beacon.StreamProtocol/include/beacon/stream/version.h`
- Create: `tests/Beacon.StreamProtocol.Tests/CMakeLists.txt`
- Modify: `.github/workflows/ci.yml`
- Modify: `.gitignore`

- [x] Write a failing lock-manifest test/script check for exact MsQuic, Protobuf,
  nv-codec-header, and future Opus revisions from the source audit.
- [x] Bootstrap dependencies into a normal ignored `_deps` directory with SHA verification;
  never create a submodule or symlink.
- [x] Build pinned MsQuic for Windows x64 and Android x86_64/arm64 with CMake/NDK.
- [x] Add a minimal encrypted loopback executable that exchanges one reliable message and
  one datagram using a test certificate.
- [x] Run the Android x86_64 side on `emulator-5554` against host `10.0.2.2` and record
  structured success. Recover/recreate the emulator first if it remains offline.
- [x] Treat Android MsQuic build or interoperability failure as a blocking source-decision
  failure. Do not hide it behind a second transport.
- [x] Run:

```powershell
cmake --preset windows-x64
cmake --build --preset windows-x64-debug
ctest --preset windows-x64-debug --output-on-failure
& $env:ANDROID_HOME\platform-tools\adb.exe -s emulator-5554 get-state
& $env:ANDROID_HOME\platform-tools\adb.exe -s emulator-5554 shell am instrument -w dev.beacon.android.test/android.test.InstrumentationTestRunner
```

- [x] Commit: `build: establish Beacon native streaming toolchain`

### Task 3: Define Typed IPC And Stream Contracts

**Files:**

- Create: `contracts/worker_ipc.proto`
- Create: `contracts/stream_control.proto`
- Create: `contracts/media_datagram_v1.md`
- Create: `src/Beacon.StreamWorker.Contracts/Beacon.StreamWorker.Contracts.csproj`
- Create: `src/Beacon.StreamProtocol/include/beacon/stream/media_datagram.h`
- Create: `src/Beacon.StreamProtocol/src/media_datagram.cpp`
- Create: `tests/Beacon.StreamWorker.Contracts.Tests/*`
- Create: `tests/Beacon.StreamProtocol.Tests/media_datagram_tests.cpp`
- Modify: `Beacon.slnx`

- [x] Write failing golden-vector tests for Protobuf length framing, version rejection,
  unknown-field tolerance, maximum message size, media-header network byte order, malformed
  chunks, and redacted ticket rendering.
- [x] Define the exact messages from the source audit. Keep Worker IPC and public stream
  control in separate schemas.
- [x] Generate C# and C++ code during build from one schema source. Generated outputs are not
  hand-edited.
- [x] Implement the fixed 40-byte media header and bounds checks without heap allocation.
- [x] Add a static test proving public envelopes contain no executable path, backend,
  protocol selector, launch URI, raw policy, or long-lived credential.
- [x] Run native CTest plus `.NET` contract tests.
- [x] Commit: `feat: define Beacon worker and stream contracts`

### Task 4: Build Deterministic In-Memory Transport And Session State Machine

**Files:**

- Create: `src/Beacon.StreamProtocol/include/beacon/stream/transport.h`
- Create: `src/Beacon.StreamProtocol/src/session.cpp`
- Create: `src/Beacon.StreamProtocol/src/frame_assembler.cpp`
- Create: `tests/Beacon.StreamProtocol.Tests/fake_transport.*`
- Create: `tests/Beacon.StreamProtocol.Tests/session_tests.cpp`
- Create: `tests/Beacon.StreamProtocol.Tests/frame_assembler_tests.cpp`

- [x] Write failing tests for ticket acceptance once, wrong client/session/plan rejection,
  explicit stop, duplicate stop, connection loss, reconnect with a new ticket, and shutdown
  resource release exactly once.
- [x] Write failing deterministic packet tests for reorder, duplicate, overlap, malformed
  offset, missing chunk, capacity eviction, IDR request, non-IDR suppression while awaiting
  recovery, and complete-IDR recovery.
- [x] Implement `IStreamTransport` and an in-memory transport whose faults are selected by
  packet sequence. Do not model faults with sleep or wall-clock expiry.
- [x] Bound assembly to four incomplete video frames and the planned-size/16 MiB ceiling.
- [x] Expose state transitions and metrics as typed events, not log-text parsing.
- [x] Commit: `feat: add deterministic Beacon stream state machine`

### Task 5: Build StreamWorker Process And Event-Driven Named-Pipe IPC

**Files:**

- Create: `src/Beacon.StreamWorker/CMakeLists.txt`
- Create: `src/Beacon.StreamWorker/src/main.cpp`
- Create: `src/Beacon.StreamWorker/src/worker_host.*`
- Create: `src/Beacon.StreamWorker/src/named_pipe_channel.*`
- Create: `src/Beacon.Platform.Windows/Streaming/StreamWorkerProcessHost.cs`
- Create: `src/Beacon.Platform.Windows/Streaming/StreamWorkerNamedPipeClient.cs`
- Create: `src/Beacon.Platform.Windows/Streaming/StreamWorkerStreamingBackend.cs`
- Create: `tests/Beacon.StreamWorker.Tests/*`
- Create: `tests/Beacon.Platform.Windows.Tests/Streaming/*`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`

- [x] Write failing tests for owner-only pipe creation, Worker hello/version mismatch,
  readiness acknowledgement, concurrent request correlation, malformed/oversized frame,
  explicit shutdown, Worker process exit, and diagnostic secret redaction.
- [x] Launch one Worker in the interactive session and assign it to a kill-on-Service-close
  Job Object. Do not terminate it on stream disconnect.
- [x] Use overlapped pipe I/O and process-handle events. A command completes from its typed
  response or Worker process exit; no startup sleep, descriptor poll, or timeout decides it.
- [x] Implement a fake capture/encoder source inside Worker that emits deterministic numbered
  access units through `IStreamTransport`.
- [x] Replace `UnavailableStreamingBackend` only after Worker readiness is proven. Keep
  `FakeStreamingBackend` restricted to fake-host tests.
- [x] Commit: `feat: host Beacon StreamWorker through typed IPC`

### Task 6: Harden Control-Plane Identity And Single-Use Tickets

**Files:**

- Create: `src/Beacon.Server/Security/BeaconServerIdentity.cs`
- Create: `src/Beacon.Server/Security/ClientCredentialService.cs`
- Create: `src/Beacon.Server/Security/StreamTicketService.cs`
- Create: `src/Beacon.Server/State/StreamTicketRecord.cs`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconCredentialStore.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidKeyStoreCredentialStore.java`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs`
- Modify: `src/Beacon.Cockpit/MainWindow.xaml`
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconApiClient.java`
- Modify: server and Android security tests

- [x] Write failing tests for persistent server identity, public-key pin mismatch, initial
  pending registration, explicit trusted-Cockpit approval, per-client credential hashing,
  authenticated and client-scoped profile/catalog/launch/recovery calls,
  ticket entropy, hash-only Worker provisioning, client/session/plan binding, single use,
  Worker-instance binding, security expiry validation, revocation, reconnect ticket
  replacement, and log redaction.
- [x] Require HTTPS and a pinned Beacon public-key fingerprint outside explicit test-host
  mode. Existing plain HTTP remains test-only.
- [x] Replace shared-token auto-registration with a pending request that only the local
  Cockpit can approve. Approval completion is event-driven; it is not a polling loop.
- [x] Store the Android client credential wrapped by Android Keystore; never store the raw
  pairing token after enrollment.
- [x] Make ticket expiry a validation fact only. Do not schedule session teardown from it.
- [x] Commit: `feat: authenticate Beacon clients and stream tickets`

### Task 7: Implement The Real MsQuic Transport

**Files:**

- Create: `src/Beacon.StreamProtocol/include/beacon/stream/msquic_transport.h`
- Create: `src/Beacon.StreamProtocol/src/msquic_transport.cpp`
- Create: `src/Beacon.StreamWorker/src/quic_listener.*`
- Create: `tests/Beacon.StreamProtocol.Tests/msquic_transport_tests.cpp`
- Create: `tests/Beacon.StreamWorker.Tests/quic_session_tests.cpp`

- [x] Write failing loopback tests for certificate validation, ALPN/version mismatch,
  ticket replay, independent session/input/feedback streams, datagram negotiation,
  `MaxSendLength` chunk sizing, send-state loss evidence, graceful close, abortive network
  close, and reconnect with a fresh ticket.
- [x] Configure one QUIC connection with reliable streams and datagram receive enabled.
  Disable transport idle expiry as an ownership mechanism; explicit state/events own the
  session.
- [x] Use MsQuic send-complete callbacks for buffer ownership. Never retain stack buffers or
  assume send completion means peer receipt.
- [x] Feed QUIC statistics and client feedback into typed metrics without adding adaptive
  policy yet.
- [x] Commit: `feat: add authenticated Beacon QUIC transport`

### Task 8: Introduce One Android StreamCore Route

**Files:**

- Modify: `contracts/worker_ipc.proto`
- Modify: Worker/Service streaming state and launch/reconnect API tests
- Create: `src/Beacon.Android/app/src/main/cpp/CMakeLists.txt`
- Create: `src/Beacon.Android/app/src/main/cpp/streamcore/*`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconStreamCore.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconStreamSession.java`
- Create: Android unit/instrumentation tests for StreamCore
- Modify: `src/Beacon.Android/app/build.gradle`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`

- [x] Write failing JNI/native tests for lifecycle, certificate pin, ticket handoff,
  control/input/feedback stream routing, frame callback thread ownership, Surface replacement,
  connection loss, explicit stop, and release exactly once.
- [x] Return the actual ephemeral Worker listener port through a typed Worker IPC event and
  the launch/reconnect connection grant. Include the session id, pinned server public-key
  fingerprint, authoritative selected-video mode needed by the native StartSession handshake,
  and plan explanation; derive the host from the already configured control-plane route. Never guess a
  fixed port or reintroduce an endpoint-role map, launch URI, descriptor, or polling path.
- [x] Build native MsQuic/Protobuf into the APK for x86_64 emulator and arm64 physical target.
- [x] Connect the native assembler to a fake Java encoded-frame sink first. Do not invoke
  MediaCodec or add a second route in this task.
- [x] Make `BeaconViewModel` own exactly one `BeaconStreamCore`; remove any transitional API
  shape once tests move.
- [x] Verify APK stop and activity destruction release every native handle under sanitizable
  host tests and Android instrumentation.
- [x] Commit: `feat: add single Android Beacon StreamCore route`

### Task 9: Gate 3 Static And Dynamic Validation

- [x] Run full `.NET`, native CTest, Android unit/build/instrumentation, Client Lab lint/test,
  Playwright, FakeEndpoint, DisplayProbe read-only, and GameProbe validation.
- [x] Run a real Service -> named pipe -> Worker -> MsQuic -> emulator StreamCore fake-media
  session. Prove ready, connect, packet receive, input echo, feedback, disconnect, fresh-ticket
  reconnect, stop, Worker crash isolation, and emergency restore from structured state.
- [x] Run static absence checks over all source/test/build files for the prohibited upstream
  and alternate-route names.
- [x] Confirm logs contain no raw ticket, client credential, private key, executable path, or
  input payload.
- [x] Push, open a ready PR, wait for every duplicate CI job, merge, and synchronize `main`.

### Task 10: Gate 3 Refactor Audit And Second Sync

- [x] Audit only proven duplication, ownership leaks, generated-code drift, JNI resource
  imbalance, unsafe buffer lifetime, compatibility residue, and secret exposure.
- [x] Add a failing regression before each justified refactor.
- [x] Repeat Task 9 in full.
- [x] Push a second ready PR, wait for every CI job, merge, and synchronize clean `main`.

## Gate 4: Automatic And Manual Network/Hardware Benchmark

### Task 11: Define Raw Benchmark Evidence And Server Scoring

**Files:**

- Create: `src/Beacon.Core/Benchmarks/*`
- Create: `tests/Beacon.Core.Tests/Benchmarks/*`
- Modify: `src/Beacon.Core/Sessions/SessionPlanner.cs`
- Modify: client profile persistence models

- [x] Test versioned network fingerprints, hardware fingerprints, evidence invalidation,
  manual always-new runs, candidate scoring, stale-result rejection, and plan explanation.
- [x] Store raw samples and selected result server-side. The client reports facts only.
- [x] Include route/address, transport, network prefix, Wi-Fi band/channel, link-speed bucket,
  salted SSID/BSSID hash when available, codec/decode facts, display refresh, thermals, and
  battery/power state.
- [x] Test that raw SSID/BSSID remain APK-local and never enter requests, persistence,
  diagnostics, or logs. Invalidate calibration on every hardware/network/schema/version fact
  listed by `REQ-BENCH-007`.
- [x] Commit: `feat: model Beacon benchmark evidence`

### Task 12: Run Benchmark Traffic Through The Production Transport

**Files:**

- Modify: `contracts/stream_control.proto`
- Create: Worker and StreamCore benchmark modules/tests
- Modify: Android telemetry/network observers
- Modify: Client Lab benchmark simulation

- [ ] Test reliable throughput rounds, datagram loss/reorder/RTT/jitter rounds, decode-vector
  rounds, presentation evidence, thermal sampling, explicit cancellation, and network-change
  restart from a new run id.
- [ ] Trigger full calibration automatically from material fingerprint changes and manually
  from the APK. Session preflight uses a short explicitly specified measurement round; it is
  not a lifecycle timeout.
- [ ] Do not apply live bitrate adaptation yet. Feed measured evidence into the planner.
- [ ] Commit: `feat: benchmark Beacon network and hardware path`

### Task 13: Gate 4 Validate, Sync, Refactor, Validate, Sync

- [ ] Run deterministic fake benchmarks, real loopback, emulator network/hardware benchmark,
  Client Lab, and the complete regression matrix.
- [ ] Confirm the fake Z Fold 7 profile still preserves `2560x1600` and `120 Hz` intent while
  the server remains free to reject 120 FPS when measured evidence is insufficient.
- [ ] Verify automatic Wi-Fi/network fingerprint changes create a new run and unchanged
  fingerprints reuse valid evidence.
- [ ] Merge the implementation PR, audit scoring/ownership/duplicate telemetry, add
  regressions, repeat all validation, and merge the refactor PR.

## Gate 5: Minimal Real H.264 SDR Stream

### Task 14: Capture The Planned Display With WGC

**Files:**

- Create: `src/Beacon.StreamWorker/src/capture/wgc_display_capture.*`
- Create: `src/Beacon.StreamWorker/src/capture/d3d11_device.*`
- Create: capture unit/integration tests
- Modify: Worker IPC internal display-target record

- [x] Test stable monitor resolution, wrong/missing/inactive display failure, selected NVIDIA
  adapter, frame callback, QPC timestamp, content-size change, pool recreation, stop during
  callback, and resource release.
- [x] Resolve only the Service-provided internal display target; never capture physical as a
  fallback.
- [x] Add an explicit manual integration test that paints changing content on the leased
  virtual display and confirms frame hashes change.
- [x] Commit: `feat: capture planned display in StreamWorker`

Validation evidence (2026-07-14): the fresh native build passed all 16 tests, the full .NET
solution passed all 466 tests, and the manual WGC probe captured changing frame hashes with
monotonic QPC timestamps from both the 2560x1600 physical display and a temporary
2560x1600@60 SudoVDA lease while selecting the NVIDIA RTX 4090 adapter.

### Task 15: Convert And Scale On D3D11

**Files:**

- Create: `src/Beacon.StreamWorker/src/video/d3d11_video_processor.*`
- Create: conversion tests and a GPU integration probe

- [ ] Test exact capability query, planned crop/aspect behavior, BGRA-to-NV12 conversion,
  output dimensions, BT.709 limited metadata, texture reuse, device loss, and unsupported
  capability failure.
- [ ] Use `ID3D11VideoProcessor`/`VideoProcessorBlt` on the capture device. Do not add a CPU or
  shader fallback.
- [ ] Validate generated NV12 planes against deterministic color bars within declared
  tolerances.
- [ ] Commit: `feat: convert Beacon frames on D3D11`

### Task 16: Encode H.264 With Native NVENC

**Files:**

- Create: `src/Beacon.StreamWorker/src/video/nvenc_h264_encoder.*`
- Vendor: pinned `nvEncodeAPI.h` plus license notice
- Create: encoder unit/integration tests

- [ ] Test DLL/API/capability preflight, D3D11 texture registration, low-latency no-B-frame
  configuration, SPS/PPS + IDR first frame, monotonic timestamps, forced IDR, bitrate
  reconfiguration, encode failure, and exact cleanup ordering.
- [ ] Validate encoded Annex-B access units with a test decoder/probe only; that probe is a
  test tool, not a production alternate route.
- [ ] Commit: `feat: encode Beacon H264 with NVENC`

### Task 17: Packetize Real Access Units And Apply Recovery

**Files:**

- Create: `src/Beacon.StreamWorker/src/video/media_packetizer.*`
- Modify: Worker media session and rate controller
- Extend: StreamProtocol tests

- [ ] Test packetization at negotiated `MaxSendLength`, complete reconstruction, SPS/PPS/IDR
  flags, datagram loss notification, reliable IDR request, bitrate bounds, queue-pressure
  reduction, and recovery without media retransmission.
- [ ] Drive NVENC bitrate reconfiguration only from server-plan bounds plus typed QUIC/client
  evidence. Record every decision and input fact.
- [ ] Commit: `feat: stream Beacon H264 access units`

### Task 18: Decode And Present Through Android MediaCodec

**Files:**

- Modify: `BeaconStreamCore.java`
- Modify: `SurfaceEncodedVideoDecoder.java`
- Modify: `AndroidSurfaceViewProvider.java`
- Delete after replacement: Android and server `AnnexBAccessUnitSplitter` plus envelope code
- Create: decoder unit/instrumentation tests

- [ ] Test asynchronous MediaCodec configuration, low-latency capability gating, direct
  access-unit queue, codec-config/IDR start, PTS propagation, Surface lifecycle, bounded queue
  drop, awaiting-IDR recovery, decoder failure feedback, stop/reconnect, and release once.
- [ ] Delete both old splitters only after tests prove StreamCore receives complete access
  units.
- [ ] On emulator, require structured first-frame and moving-frame evidence; do not infer
  success only from an HTTP response or nonblank Surface.
- [ ] Commit: `feat: render Beacon H264 in StreamCore`

### Task 19: Integrate Launch, Reconnect, Stop, And Restore Transaction

**Files:**

- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Modify: streaming/session/display orchestration services
- Modify: Android ViewModel/session tests
- Modify: Client Lab and FakeEndpoint flows

- [ ] Test the ordered startup transaction from the specification and reverse compensation
  for every failure point.
- [ ] Prove unexpected transport loss stops media resources but does not terminate the app or
  display lease. A still-beaconing client obtains a fresh ticket and reconnects to the same
  server-owned session.
- [ ] Prove explicit stop and session closure obey inactive **AND** no-owned-work before lease
  removal and physical-primary restore.
- [ ] Prove Worker crash reports failure without stranding display ownership and allows an
  explicit/reconnect-driven Worker restart.
- [ ] Commit: `feat: complete Beacon stream session transaction`

### Task 20: Gate 5 Full Dynamic Acceptance

- [ ] Stop Apollo and Sunshine and prove neither process, port, file, nor API is used.
- [ ] Use Client Lab and the APK emulator to select a real catalog application.
- [ ] Create/activate the correct per-client virtual display at the planned mode.
- [ ] Show moving H.264 SDR video from that display on `emulator-5554` through StreamCore.
- [ ] Verify input reaches the leased display through the authenticated input stream.
- [ ] Disconnect/reconnect with a new ticket while preserving app/display ownership.
- [ ] Close the app, stop the session, and verify physical primary restore under the
  inactive **AND** no-owned-work rule.
- [ ] Exercise Worker crash, decoder error, network loss, emergency restore, and repeated
  fast connect/disconnect from structured events.
- [ ] Run full static validation and prohibited-route searches.
- [ ] Merge the implementation PR only after all duplicate CI jobs pass.

### Task 21: Gate 5 Refactor Audit And Final Sync

- [ ] Audit frame/buffer ownership, D3D/NVENC cleanup, JNI/Surface lifecycle, reconnect state,
  session compensation, diagnostic truth, dead generic decoder primitives, and source
  provenance.
- [ ] Add regressions before fixes and remove only abstractions proven dead or duplicate.
- [ ] Repeat Task 20 and the complete static/dynamic matrix.
- [ ] Merge the refactor PR, synchronize clean `main`, and update the authoritative spec,
  extraction map, source audit, and README with observed evidence.

## Deferred Until Gate 5 Passes

Create separate audited plans, in this order, for:

1. event-driven WASAPI loopback + Opus + AAudio;
2. richer controller and multitouch behavior;
3. HEVC and AV1 through the same encoder/protocol/decoder contracts;
4. HDR chain capability and truthful fallback;
5. physical Z Fold 7 benchmark, 120 Hz, thermals, Wi-Fi, input, audio, and UX;
6. UI refinement that does not expose server-owned stream/display policy.

## Gate 5 Exit Evidence

- One production StreamWorker and one APK StreamCore route exist.
- Service-to-Worker readiness and state are typed, private, and event-driven.
- Worker-to-StreamCore traffic is authenticated, encrypted, congestion controlled, and
  Beacon-owned.
- Emulator displays moving H.264 SDR from the planned virtual display with Apollo and
  Sunshine stopped.
- Reconnect uses a fresh ticket without destroying application/display ownership.
- Explicit stop and ownership cleanup restore verified physical-primary state.
- No alternate transport, backend, capture fallback, media route, or compatibility artifact
  exists.
- Static and dynamic validation pass twice around the refactor sync.
