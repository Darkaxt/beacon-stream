# Production Video Pipeline Validation

Date: 2026-07-14

Scope: first implementation sync for the Beacon-owned Windows production video path. This
evidence does not claim APK/emulator moving-video acceptance.

Implementation sync: `e9ba8e9` (`feat: compose Beacon production video pipeline`).

## Implemented Boundary

- Stream tickets are bound to one exact prepared H.264 SDR video mode.
- `WorkerVideoPipeline` owns reconnectable generation state while preserving the prepared plan.
- `ProductionVideoGeneration` composes WGC, D3D11 NV12 conversion, NVENC, rate control,
  packetization, and generation-bound QUIC delivery.
- Production `QuicListener` no longer injects a synthetic access-unit marker.
- Worker pipeline failures publish typed diagnostics and disconnect only the active QUIC client.

## Dynamic Evidence

Command:

```powershell
.\scripts\test-stream-worker-integration.ps1
```

Observed against read-only primary display `\\.\DISPLAY5` at `2560x1600`:

```text
BEACON_WORKER_IPC_QUIC_OK AUTH INPUT FEEDBACK REAL_H264_ACCESS_UNIT DISCONNECT SHUTDOWN
BEACON_WORKER_STARTUP_EXIT 64
```

The probe launches the production Worker, prepares a `2560x1600@120` H.264 SDR plan, captures
the existing physical display without changing topology, requires an IDR/SPS/PPS-bearing real
H.264 datagram, observes input and feedback, then verifies disconnect and explicit shutdown.

## Static And Build Evidence

- `dotnet format Beacon.slnx --verify-no-changes --no-restore`: passed.
- `dotnet build Beacon.slnx -warnaserror --no-restore`: passed with zero warnings.
- `dotnet test Beacon.slnx --no-build`: 506 passed.
- `scripts/build-native-windows.ps1`: build passed; 24/24 CTests passed.
- `scripts/test-android.ps1 -Tasks clean,test,assembleDebug,assembleRelease,assembleDebugAndroidTest`:
  passed; 118 Gradle tasks completed and x86_64/arm64 native StreamCore artifacts built.
- Client Lab: lint passed; 16 unit tests passed.
- Client Lab Playwright: lint passed; one full simulated lifecycle test passed.
- `FakeEndpointLiveTests`: 2 passed.
- read-only DisplayProbe: SudoVDA ready, mirror mode false, physical primary verified.
- read-only GameProbe: catalog scan completed.
- `scripts/test-gate3.ps1 -ValidateFixtures`: architecture absence, secret fixtures, and Kestrel
  parser fixtures passed.
- `git diff --check`: passed.

## Deferred Acceptance

No ADB or emulator command was issued because another task owns the shared emulator. The Gate 5
moving-video, APK rendering, reconnect, virtual-display, and restore transaction remains open.
No Apollo process, service, file, API, configuration, or installation was queried or changed.

## Refactor Validation

The ownership refactor was validated on 2026-07-14 with the same non-device matrix:

- Generation creation and startup run outside the controller mutex. A condition-variable
  regression proves disconnect during in-flight startup abandons and stops that generation
  exactly once while preserving the reconnect plan.
- Worker callbacks hold shared dispatcher state and a weak pipeline reference, so transport
  callbacks cannot outlive captured stack objects or the pipeline.
- Pipeline failure contracts moved to a narrow header; `worker_events.h` no longer imports the
  production capture/encoder composition.
- The synthetic media source, implementation, and test target were deleted from the production
  tree. Architecture tests prevent reintroduction.
- The process client now reassembles the complete first frame and verifies Annex B SPS, PPS,
  and IDR NAL units before reporting `REAL_H264_ACCESS_UNIT`.

Repeated results:

- .NET format and warning-as-error build passed; 509 tests passed.
- Windows native build passed; 23/23 CTests passed.
- Worker IPC/QUIC process probe passed with complete real H.264 access-unit validation.
- Android clean/unit/debug/release/instrumentation-package build passed; 118 Gradle tasks.
- Client Lab lint and 16 tests passed; Playwright lifecycle passed.
- Fake endpoint, read-only display/game probes, architecture absence, secret fixtures, and
  Kestrel parser fixtures passed.
- No ADB, emulator, Apollo, or display-topology command was used.
