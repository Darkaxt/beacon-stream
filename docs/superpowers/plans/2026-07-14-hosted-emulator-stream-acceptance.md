# Beacon Hosted Emulator Stream Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove changing H.264 pixels travel through Beacon's authenticated QUIC protocol and the APK's sole production StreamCore/MediaCodec/SurfaceView path on an isolated GitHub-hosted Android emulator.

**Architecture:** Move the server session state machine, secure byte clearing, and media packetizer into the platform-neutral `Beacon.StreamProtocol` library. Keep Windows ticket hashing and Worker ownership in `Beacon.StreamWorker`, then build a test-only Android-native MsQuic endpoint from the shared protocol primitives and validate changing `SurfaceView` pixels with `PixelCopy`.

**Tech Stack:** C++20, CMake/CTest, Protobuf, MsQuic/QuicTLS, Java 17, Android instrumentation, MediaCodec, SurfaceView, PixelCopy, Bash, GitHub Actions.

---

### Task 1: Extract The Server Session State Machine

**Files:**
- Create: `src/Beacon.StreamProtocol/include/beacon/stream/secure_bytes.h`
- Create: `src/Beacon.StreamProtocol/src/secure_bytes.cpp`
- Create: `src/Beacon.StreamProtocol/include/beacon/stream/stream_ticket_authorizer.h`
- Create: `src/Beacon.StreamProtocol/include/beacon/stream/server_session_protocol.h`
- Create: `src/Beacon.StreamProtocol/src/server_session_protocol.cpp`
- Create: `tests/Beacon.StreamProtocol.Tests/server_session_protocol_tests.cpp`
- Create: `tests/Beacon.StreamWorker.Tests/quic_ticket_store_tests.cpp`
- Modify: `src/Beacon.StreamProtocol/CMakeLists.txt`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/quic_ticket_store.h`
- Modify: `src/Beacon.StreamWorker/src/quic_ticket_store.cpp`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/quic_listener.h`
- Modify: `src/Beacon.StreamWorker/src/quic_listener.cpp`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/video/worker_video_pipeline.h`
- Modify: `src/Beacon.StreamWorker/src/video/worker_video_pipeline.cpp`
- Modify: `src/Beacon.StreamWorker/src/video/quic_media_rate_adapter.cpp`
- Modify: `tests/Beacon.StreamProtocol.Tests/CMakeLists.txt`
- Modify: `tests/Beacon.StreamWorker.Tests/CMakeLists.txt`
- Delete: `src/Beacon.StreamWorker/include/beacon/worker/quic_session_protocol.h`
- Delete: `src/Beacon.StreamWorker/src/quic_session_protocol.cpp`
- Delete: `src/Beacon.StreamWorker/include/beacon/worker/secure_bytes.h`
- Delete: `src/Beacon.StreamWorker/src/secure_bytes.cpp`
- Delete: `tests/Beacon.StreamWorker.Tests/quic_session_tests.cpp`

- [x] **Step 1: Write the platform-neutral protocol tests**

Move every session parsing, exact plan binding, generation, replay, stale callback, input,
feedback, framing, and wipe assertion from `quic_session_tests.cpp` into
`server_session_protocol_tests.cpp`. Replace the Windows store with this test authorizer:

```cpp
class RecordingAuthorizer final : public beacon::stream::IStreamTicketAuthorizer {
public:
  beacon::stream::StreamTicketAuthorization authorize(
      std::span<const std::byte> ticket,
      std::string_view client_id,
      std::string_view session_id,
      std::uint64_t plan_revision,
      std::uint64_t now_unix_ms) override;

  std::vector<std::byte> expected_ticket;
  beacon::stream::StreamTicketAuthorization next;
  std::size_t calls{};
};
```

Create focused Worker tests that retain SHA-256 hashing, authorization, replay, revoke, expiry,
and constant-time hash comparison coverage for `AuthorizedQuicTicketStore`.

- [x] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
./scripts/build-native-windows.ps1
```

Expected: compilation fails because `IStreamTicketAuthorizer`, `ServerSessionProtocol`, and the
shared secure-byte functions do not exist.

- [x] **Step 3: Implement the shared authorization contract and state machine**

Define the contract in `stream_ticket_authorizer.h`:

```cpp
enum class StreamTicketAuthorizationResult {
  accepted,
  unknown,
  replayed,
  client_mismatch,
  session_mismatch,
  plan_mismatch,
  expired,
};

struct StreamTicketAuthorization {
  StreamTicketAuthorizationResult result{StreamTicketAuthorizationResult::unknown};
  std::optional<v1::SelectedVideoMode> selected_video;
  std::optional<v1::StartBenchmark> benchmark_plan;
};

class IStreamTicketAuthorizer {
public:
  virtual ~IStreamTicketAuthorizer() = default;
  virtual StreamTicketAuthorization authorize(
      std::span<const std::byte> ticket,
      std::string_view client_id,
      std::string_view session_id,
      std::uint64_t plan_revision,
      std::uint64_t now_unix_ms) = 0;
};
```

Move `QuicSessionProtocol` to `beacon::stream::ServerSessionProtocol`, rename its output to
`ServerSessionProtocolOutput`, and depend only on `IStreamTicketAuthorizer`. Move secure clearing
to `beacon::stream`; use `SecureZeroMemory` on Windows and a volatile byte loop followed by
`std::atomic_signal_fence` elsewhere. Make `AuthorizedQuicTicketStore` implement the interface
while preserving Windows BCrypt hashing. Update every Worker consumer directly; do not leave
aliases or compatibility headers.

- [x] **Step 4: Run focused and full native tests**

Run:

```powershell
./scripts/build-native-windows.ps1
./scripts/build-native-android-wsl.ps1 -Configuration Debug
```

Expected: all Windows CTests pass and every Android native test target cross-compiles; the
server protocol tests execute on Windows and are included in the hosted emulator native suite.

- [x] **Step 5: Commit and push the protocol extraction**

```powershell
git add src/Beacon.StreamProtocol src/Beacon.StreamWorker tests/Beacon.StreamProtocol.Tests tests/Beacon.StreamWorker.Tests
git commit -m "refactor: extract Beacon server session protocol"
git push origin codex/beacon-production-benchmarks
```

### Task 2: Extract Beacon Video Packetization From NVENC

**Files:**
- Create: `src/Beacon.StreamProtocol/include/beacon/stream/video_media_packetizer.h`
- Create: `src/Beacon.StreamProtocol/src/video_media_packetizer.cpp`
- Create: `tests/Beacon.StreamProtocol.Tests/video_media_packetizer_tests.cpp`
- Modify: `src/Beacon.StreamProtocol/CMakeLists.txt`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/video/video_media_session.h`
- Modify: `src/Beacon.StreamWorker/src/video/video_media_session.cpp`
- Modify: `tests/Beacon.StreamProtocol.Tests/CMakeLists.txt`
- Modify: `tests/Beacon.StreamWorker.Tests/CMakeLists.txt`
- Delete: `src/Beacon.StreamWorker/include/beacon/worker/video/media_packetizer.h`
- Delete: `src/Beacon.StreamWorker/src/video/media_packetizer.cpp`
- Delete: `tests/Beacon.StreamWorker.Tests/media_packetizer_tests.cpp`

- [x] **Step 1: Move packetization tests to the shared contract and verify RED**

Express packet input without an encoder type:

```cpp
const beacon::stream::EncodedVideoAccessUnitView unit{
    .bytes = encoded_bytes,
    .idr = true,
    .codec_configuration = true,
};
const auto result = packetizer.packetize(unit, 7, 2'345'678, 1200);
```

Retain exact header, chunk ordering, end-of-unit, IDR/configuration flag, invalid sequence,
empty input, maximum frame, datagram size, and chunk-count assertions.

Run `./scripts/build-native-windows.ps1`. Expected: compile failure because the shared packetizer
does not exist.

- [x] **Step 2: Implement the shared packetizer**

Define:

```cpp
struct EncodedVideoAccessUnitView {
  std::span<const std::uint8_t> bytes;
  bool idr{};
  bool codec_configuration{};
};

class VideoMediaPacketizer final {
public:
  [[nodiscard]] VideoMediaPacketizationResult packetize(
      EncodedVideoAccessUnitView access_unit,
      std::uint64_t sequence,
      std::uint64_t presentation_time_us,
      std::uint16_t maximum_datagram_bytes) const noexcept;
};
```

Move the existing serialization implementation unchanged except for the input view. Adapt
`VideoMediaSession` at the NVENC boundary by constructing the view from `annex_b`, `idr`, and
`has_sps || has_pps`. Delete the Worker packetizer implementation rather than wrapping it.

- [x] **Step 3: Validate, commit, and push packetization**

Run:

```powershell
./scripts/build-native-windows.ps1
./scripts/test-stream-worker-integration.ps1 -AllowUnsupportedVideoHardware
./scripts/build-native-android-wsl.ps1 -Configuration Debug
```

Expected: all native tests and the real Worker process probe pass with unchanged media bytes.

```powershell
git add src/Beacon.StreamProtocol src/Beacon.StreamWorker tests/Beacon.StreamProtocol.Tests tests/Beacon.StreamWorker.Tests
git commit -m "refactor: share Beacon media packetization"
git push origin codex/beacon-production-benchmarks
```

### Task 3: Build The Test-Only Android QUIC Endpoint

**Files:**
- Create: `tests/Beacon.StreamProtocol.Tests/access_unit_vector.h`
- Create: `tests/Beacon.StreamProtocol.Tests/access_unit_vector.cpp`
- Create: `tests/Beacon.StreamProtocol.Tests/access_unit_vector_tests.cpp`
- Create: `tests/Beacon.StreamProtocol.Tests/hosted_emulator_endpoint_server.h`
- Create: `tests/Beacon.StreamProtocol.Tests/hosted_emulator_endpoint_server.cpp`
- Create: `tests/Beacon.StreamProtocol.Tests/hosted_emulator_endpoint.cpp`
- Modify: `tests/Beacon.StreamProtocol.Tests/CMakeLists.txt`
- Modify: `scripts/test-native-android-protocol.sh`

- [x] **Step 1: Write failing BAU parser and endpoint contract tests**

Test `BEACONAU1\n`, little-endian frame count/lengths, truncation, trailing bytes, empty units,
and the checked-in 640x360 vector's exact 30-unit count. Add a test authorizer asserting the
endpoint accepts only:

```text
client: hosted-emulator
session: hosted-emulator-stream
plan revision: 1
ticket bytes: beacon-hosted-emulator-ticket-v1
video: h264, 640x360, 30/1, sdr
```

Run the Android native build. Expected: compile failure because the parser and endpoint server
do not exist.

- [x] **Step 2: Implement the event-driven MsQuic endpoint**

The endpoint accepts `--certificate`, `--private-key`, and `--vector`, listens on
`127.0.0.1:0`, and prints `BEACON_HOSTED_ENDPOINT_READY <port>` only after
`ListenerStart` succeeds. Use `ServerSessionProtocol` for every peer stream and
`VideoMediaPacketizer` for every datagram. On accepted start, send unit 1. On each matching
rendered-frame feedback event, send the next unit. On accepted stop, close the connection and
listener, print these final lines, and exit zero:

```text
BEACON_HOSTED_ENDPOINT_AUTHENTICATED 1
BEACON_HOSTED_ENDPOINT_FRAMES 30
BEACON_HOSTED_ENDPOINT_RENDERED_FEEDBACK 30
BEACON_HOSTED_ENDPOINT_STOPPED 1
```

Every MsQuic callback publishes an event into one mutex/condition-variable state machine. Do not
sleep, poll, or add a wall-clock timeout. A transport or protocol failure sets a typed terminal
error, wakes the main thread, and exits nonzero.

- [x] **Step 3: Cross-build and run native protocol regression tests**

Run:

```powershell
./scripts/build-native-android-wsl.ps1 -Configuration Debug
./scripts/build-native-windows.ps1
```

Expected: the endpoint target cross-compiles for x86_64 Android, the BAU parser passes, and all
Windows tests remain green. Add the parser test executable to
`test-native-android-protocol.sh`; do not start the endpoint in this unit-test script.

- [x] **Step 4: Commit and push the endpoint**

```powershell
git add tests/Beacon.StreamProtocol.Tests scripts/test-native-android-protocol.sh
git commit -m "test: add Beacon hosted emulator endpoint"
git push origin codex/beacon-production-benchmarks
```

### Task 4: Prove Changing SurfaceView Pixels In Instrumentation

**Files:**
- Create: `src/Beacon.Android/app/src/androidTest/java/dev/beacon/android/HostedEmulatorStreamInstrumentationTest.java`
- Create: `src/Beacon.Android/app/src/androidTest/java/dev/beacon/android/SurfaceFrameEvidence.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`

- [x] **Step 1: Write the failing hosted-stream instrumentation test**

Add one method named `rendersChangingH264FramesThroughProductionStreamCore`. The general device
suite skips it unless `hostedStreamEndpointPort` and `hostedStreamPublicKeyFingerprint` are
present. An exact invocation must launch `BeaconActivity`, obtain its `SurfaceView`, start the
normal `BeaconStreamCore`, `BeaconVideoPipeline`, and `AndroidMediaCodecFactory`, and construct a
grant for the fixed test identity and ticket.

`SurfaceFrameEvidence` implements the pipeline observer around the normal feedback sink. For
each rendered sequence it requests `PixelCopy` into a 64x36 ARGB bitmap, stores a stable pixel
checksum, then forwards rendered feedback so the endpoint releases the next frame. It completes
only after 30 rendered frames or a typed transport, decoder, or PixelCopy failure.

Run:

```powershell
./scripts/test-android.ps1 -Tasks assembleDebugAndroidTest
```

Expected: compilation fails because the activity does not expose its stream `SurfaceView` and
`SurfaceFrameEvidence` does not exist.

- [x] **Step 2: Add the narrow instrumentation hook and evidence collector**

Retain the actual `SurfaceView` created by `touchSurface()` in a field and expose only this
package-private method:

```java
SurfaceView videoSurfaceViewForInstrumentation() {
    return videoSurfaceView;
}
```

Keep every asynchronous assertion on the instrumentation thread. Callback threads store
evidence or failure and release one event latch; no callback asserts before signaling. Success
assertions are:

```java
assertEquals(30, evidence.renderedSequences().size());
assertTrue(evidence.pixelChecksums().stream().allMatch(value -> value != 0));
assertTrue(evidence.pixelChecksums().stream().distinct().count() >= 2);
assertTrue(core.callbackExecutorShutdown());
assertTrue(activity.isDestroyed());
```

- [x] **Step 3: Compile all Android variants**

Run:

```powershell
./scripts/test-android.ps1 -Tasks test,assembleDebug,assembleRelease,assembleDebugAndroidTest
```

Expected: debug and release JVM suites, both APKs, and the instrumentation APK pass locally
without launching local ADB.

- [x] **Step 4: Commit and push instrumentation**

```powershell
git add src/Beacon.Android/app/src/main src/Beacon.Android/app/src/androidTest
git commit -m "test: verify Beacon SurfaceView stream pixels"
git push origin codex/beacon-production-benchmarks
```

### Task 5: Orchestrate The Hosted Emulator Acceptance

**Files:**
- Create: `scripts/test-hosted-emulator-stream.sh`
- Modify: `.github/workflows/ci.yml`
- Create: `docs/validation/2026-07-14-hosted-emulator-stream.md`
- Modify: `docs/superpowers/plans/2026-07-10-beacon-stream-gates-3-5.md`

- [ ] **Step 1: Add a failing shell fixture validation**

Make the script reject a missing endpoint binary, MsQuic library, H.264 vector, `openssl`, or
`adb`. Run it outside an emulator and confirm it fails before any file push with a specific
missing-device diagnostic.

- [ ] **Step 2: Implement event-driven emulator orchestration**

Generate an ephemeral localhost certificate/key with `openssl`, calculate the SHA-256 SPKI pin,
push the endpoint, `libmsquic.so`, certificate, key, and 640x360 vector under
`/data/local/tmp/beacon-hosted-stream`, and start the endpoint through a Bash `coproc`. Read its
readiness line directly from the coprocess output, parse the dynamic port, and invoke exactly:

```text
dev.beacon.android.HostedEmulatorStreamInstrumentationTest#rendersChangingH264FramesThroughProductionStreamCore
```

Pass only the port and public fingerprint as instrumentation arguments. Read the endpoint's
final evidence, wait for its process exit, require every expected marker, and remove all pushed
test files in a shell trap. Do not use polling loops, readiness sleeps, or timeout-based process
termination.

- [ ] **Step 3: Wire the script after device-local instrumentation**

Update the emulator action script to run:

```yaml
script: |
  bash scripts/test-native-android-protocol.sh
  gradle -p src/Beacon.Android connectedDebugAndroidTest
  bash scripts/test-hosted-emulator-stream.sh
```

The endpoint target and instrumentation test remain test artifacts. Verify the release APK does
not contain endpoint symbols, certificate files, private keys, or BAU fixture path metadata.

- [ ] **Step 4: Run the complete local static matrix**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
dotnet test Beacon.slnx --no-build
./scripts/test-gate3.ps1 -ValidateFixtures
./scripts/build-native-windows.ps1
./scripts/test-stream-worker-integration.ps1 -AllowUnsupportedVideoHardware
./scripts/test-android.ps1 -Tasks test,assembleDebug,assembleRelease,assembleDebugAndroidTest
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright lint
pnpm --dir tests/Beacon.ClientLab.Playwright test
git diff --check
```

Expected: all static and dynamic host-independent checks pass; local ADB remains untouched.

- [ ] **Step 5: Commit, push, and validate GitHub Actions**

```powershell
git add .github/workflows/ci.yml scripts/test-hosted-emulator-stream.sh docs/validation/2026-07-14-hosted-emulator-stream.md docs/superpowers/plans/2026-07-10-beacon-stream-gates-3-5.md
git commit -m "test: validate Beacon stream on hosted emulator"
git push origin codex/beacon-production-benchmarks
```

Require all four CI jobs green. The Android log must contain 30 rendered frames, at least two
pixel checksums, 30 accepted feedback events, and clean stop evidence.

### Task 6: Refactor And Repeat The Acceptance Matrix

**Files:**
- Modify only files identified by the post-implementation ownership audit
- Modify: `docs/validation/2026-07-14-hosted-emulator-stream.md`

- [ ] **Step 1: Audit the finished boundary**

Inspect shared protocol ownership, Worker adapters, endpoint-only code, callback lifetime,
MsQuic handle closure, secret absence, APK contents, stale generation rejection, and every
error-to-waiter path. Add a failing regression before each behavioral correction. Delete dead
Worker protocol or packetizer files; do not keep aliases, wrappers, or duplicate tests.

- [ ] **Step 2: Run the complete matrix again**

Repeat every command from Task 5 Step 4 and require a second green hosted Android run with the
same wire, pixel, feedback, and teardown evidence.

- [ ] **Step 3: Record final evidence, commit, and push**

```powershell
git add src tests scripts .github docs
git commit -m "refactor: harden hosted Beacon stream acceptance"
git push origin codex/beacon-production-benchmarks
```

The slice is complete only when the second current-head CI run is green and the worktree is
clean. This does not claim Windows display, WGC, NVENC, launch, reconnect, or restore acceptance;
those remain proven by their separate Windows gates.
