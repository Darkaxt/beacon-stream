# Native Media Packetization And Recovery Validation

Date: 2026-07-14

## Scope

Task 17 adds the Beacon-owned boundary from encoded H.264 access units to generation-scoped
QUIC datagrams. It includes negotiated-size packetization, transport and client evidence
adaptation, bounded bitrate reduction, reliable IDR recovery, and complete decision journals.

The implementation has these enforced properties:

- Every datagram fits the negotiated QUIC `MaxSendLength`; complete access units reconstruct
  through the production `FrameAssembler` contract.
- SPS/PPS, IDR, and end-of-access-unit flags survive packetization.
- Media is sent once. Loss, queue pressure, and decoder recovery request a future IDR and may
  reduce bitrate, but never retransmit an old access unit.
- Rate changes stay within the server plan. Typed QUIC and client evidence is generation-scoped,
  stale evidence is rejected, and every input plus decision is retained for diagnostics.
- An access unit accepted by NVENC is not discarded merely because control changed concurrently.
  Pending control remains armed for the next encode, and failed transport generations cannot be
  reused.
- Coalesced session actions retain wire order. The real loopback covers
  `Start -> Stop -> Start -> IDR` and proves only the final active start emits media.
- Normal media diagnostics validate the Beacon datagram header and report its actual presentation
  timestamp rather than the synthetic startup-marker timestamp.

## Native Tests

Commands:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\Tools\Launch-VsDevShell.ps1' `
    -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
& 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' `
    --build native\out\build\windows-x64 --config Debug
& 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe' `
    --test-dir native\out\build\windows-x64 -C Debug --output-on-failure
```

Observed result:

```text
100% tests passed, 0 tests failed out of 22
```

The focused suites cover packet-size boundaries, reconstruction, flags, malformed inputs,
out-of-order and duplicate loss evidence, queue hysteresis, minimum bitrate, stale generations,
transport closure, packetizer failure, pending-control supersession, and recovery without replay.

## Dynamic Transport And Encoder

Commands:

```powershell
& .\scripts\test-quic-listener.ps1
& .\scripts\test-nvenc-h264.ps1
```

Observed output:

```text
BEACON_QUIC_LOOPBACK_OK 6 CERT_PIN_OK ALPN_VERSION_OK REPLAY_RECONNECT_OK MEDIA_RECOVERY_EVENTS_OK ORDERED_SESSION_ACTIONS_OK CALLBACK_FAULTS_OK DISCONNECT_FAULTS_OK
BEACON_NVENC_H264_OK adapter="NVIDIA GeForce RTX 4090 Laptop GPU" input=1280x720 output=640x400 frames=4 bytes=1909 first_idr=1 forced_idr=1 bitrate_reconfigure=1
BEACON_NVENC_H264_VALIDATION_OK frames=4 decoded=4
```

The loopback uses a real MsQuic client, certificate pinning, ordered/coalesced session actions,
generation-scoped sends, ACK/loss telemetry, and a second valid media datagram whose
`2,345,678 us` PTS must reach Worker diagnostics unchanged. The encoder probe uses the laptop's
RTX 4090 and a test-only FFmpeg decode oracle.

## Repository Gates

`dotnet format Beacon.slnx --verify-no-changes --no-restore` and
`dotnet test Beacon.slnx --no-restore` pass with all 487 managed tests. PowerShell parsing,
`git diff --check`, all newly created C++ formatting checks, and the prohibited production-route
scan are clean. There are no Apollo, Sunshine, GameStream, Moonlight, Vibepollo, or Vibeshine
references under production `src` or `contracts`.

This validation did not inspect, query, start, stop, configure, or otherwise interact with an
Apollo installation. It did not create or modify a display, access a display driver, or use ADB or
an emulator.

Task 17 does not claim Android decode/presentation or the final captured-desktop session
transaction. Those remain Tasks 18 and 19.

## Post-Sync Boundary Audit

The implementation sync is commit `7054ce4` (`feat: stream Beacon H264 access units`). The
required refactor audit found that `IWorkerMediaTransport` still inherited the older generic
`IStreamTransport`, which exposed a generation-free `send(packet)` method alongside the new
generation-scoped API. WorkerHost never used that method.

The refactor removes the generic inheritance and `QuicListener::send`. StreamWorker now exposes
only listener lifecycle operations and `send_for_generation` for media. A compile-time assertion
prevents the unsafe inheritance from returning, and a source scan verifies there is no
generation-free Worker media-send implementation. The complete native, dynamic, managed, and
static matrix above passes again after this change.
