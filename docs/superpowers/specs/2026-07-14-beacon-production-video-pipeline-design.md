# Beacon Production Video Pipeline Design

Status: approved architecture-recovery slice, 2026-07-14

## Purpose

Gate 5 has individually validated implementations for Windows Graphics Capture, D3D11
BGRA-to-NV12 conversion, NVENC H.264 encoding, access-unit packetization, QUIC transport,
rate control, and Android MediaCodec presentation. The production StreamWorker does not yet
compose those pieces. Its current `start_media` path only opens the QUIC listener, so the
process-level probe can prove authentication and a synthetic access-unit marker but cannot
produce moving desktop video.

This slice creates the one Beacon-owned production path:

```text
immutable Worker video plan
-> authenticated Beacon QUIC StartSession
-> WGC capture of the exact planned display
-> D3D11 BGRA-to-NV12 conversion
-> NVENC H.264 access unit
-> VideoMediaSession packetization and rate control
-> generation-bound QUIC datagrams
-> Android StreamCore
```

It does not add a second backend, external wrapper, compatibility protocol, capture fallback,
or client-selected policy. It does not inspect, invoke, configure, or control Apollo,
Sunshine, or any related installation.

## Decisions

### Generation-Scoped Composition

The production path consists of two ownership levels:

1. `WorkerVideoPipeline` retains the immutable prepared plan while the Worker listener is
   available. It receives typed QUIC media events and owns at most one active generation.
2. `ProductionVideoGeneration` owns WGC, D3D11 processing, NVENC, and `VideoMediaSession` for
   exactly one authenticated transport generation.

An accepted `StartSession` creates a fresh generation. A matching `StopSession`, transport
disconnect, explicit Worker stop, IPC loss, or Worker shutdown stops capture, drains callbacks,
and destroys that generation's GPU resources exactly once. A transport disconnect does not
discard the prepared plan or close the listener, so a fresh ticket can create a fresh
generation without relaunching the application or changing display ownership.

Fresh generation objects are required because NVENC media-thread ownership and WGC consumer
thread identity are generation-local. Reusing an encoder across reconnects would make thread
ownership accidental and would weaken deterministic cleanup. Every generation begins with an
IDR carrying SPS/PPS.

### WorkerHost Boundary

`WorkerHost` remains the owner of private IPC state. It converts `PrepareSession` into a
plain `WorkerVideoPlan`, validates resolution, frame rate, SDR H.264, display identity, and
ordered nonzero bitrate bounds, then prepares the video pipeline. The pipeline cannot discover
settings, displays, clients, applications, or policy.

`WorkerHost` forwards explicit IDR, stop, and shutdown actions to the pipeline. Benchmark
preparation clears video preparation and continues through the existing benchmark path. The
Worker's private IPC channel ending stops the pipeline before shutting down QUIC.

### Immutable Plan Binding

Each authorized QUIC ticket carries exactly one prepared operation: a selected video mode or
a benchmark plan. On authentication the session protocol retains that authorized operation.
`StartSession.selected_video` must exactly equal the server-prepared video mode before the
protocol publishes `AcceptedStartSession`. A mismatch closes the connection as a plan mismatch
and never starts capture.

The public client message contains no bitrate or display identity. Bitrate bounds and capture
target come only from `PrepareSession`, so feedback may adjust bitrate only within the
server-owned bounds already held by the generation.

### Frame Flow

`ProductionVideoGeneration` constructs the existing concrete components from their Windows
platform factories. Its WGC callback runs on the WGC consumer thread and performs, in order:

1. apply any pending bitrate/IDR control;
2. convert the captured BGRA texture to the planned NV12 dimensions;
3. encode one H.264 access unit with the requested IDR state;
4. convert WGC system-relative 100-nanosecond time to monotonic microseconds;
5. packetize and send through `VideoMediaSession` for the active generation.

The path never copies frame pixels through CPU memory and has no physical-display, software,
shader, codec, or transport fallback.

### Concurrency And Teardown

The controller copies the active generation's `shared_ptr` under a mutex and invokes it after
releasing the mutex. Generation stop first tells WGC to stop; WGC drains platform callbacks and
joins its consumer thread before processor, encoder, or media-session objects can be destroyed.
Frame callbacks hold a weak generation reference, so no callback can resurrect a stopped
generation.

Feedback, datagram outcomes, and IDR requests are accepted only when their generation matches
the active generation. Stale events are ignored. No wall-clock timeout authorizes teardown.

### Failure And Diagnostics

Invalid plans fail synchronously at `PrepareSession`. Capture, conversion, encoding, bitrate,
packetization, and transport failures are typed by the existing component enums. An asynchronous
generation failure:

- stops only the failing generation;
- publishes a failed session state plus a diagnostic identifying the native boundary;
- requests an event-driven disconnect of the active QUIC connection while leaving the listener
  available for a later fresh ticket;
- retains the immutable prepared plan for an explicit reconnect attempt.

The Worker never fabricates encoded-frame or sent-datagram metrics. Initial metrics remain zero;
subsequent evidence comes from actual encoded access units and accepted QUIC datagrams.

## Alternatives Rejected

### Put Capture And Encoding Directly In WorkerHost

This would mix IPC policy state, QUIC callbacks, GPU thread ownership, and frame processing in
one object. It would make deterministic tests harder and couple reconnect behavior to command
dispatch. `WorkerHost` remains a narrow lifecycle boundary instead.

### Add A Separate Capture Process Or Wrapper

Another process or runtime descriptor would reintroduce the external-wrapper architecture the
recovery objective removes. StreamWorker already owns the correct native primitives and is the
intended crash boundary.

### Keep One Encoder Alive Across Reconnects

This saves object construction but violates the current encoder's explicit media-thread
ownership and complicates stale callback rejection. Generation-local construction is simpler,
safer, and guarantees a fresh IDR.

## Verification Contract

Deterministic native tests must prove:

- invalid or unordered bitrate plans never prepare;
- tickets bind the exact server-selected video mode;
- capture begins only after an authenticated, matching `StartSession`;
- one fake WGC frame traverses conversion, NVENC, packetization, and the generation-bound
  datagram transport with the expected timestamp and initial IDR;
- feedback changes bitrate only inside prepared bounds and loss forces the next IDR;
- stale events cannot affect the active generation;
- disconnect destroys generation resources once but preserves the prepared plan;
- reconnect creates new GPU objects and begins with a fresh IDR;
- explicit stop, IPC loss, and shutdown release resources before transport shutdown;
- asynchronous failure produces truthful diagnostics and disconnects only the active
  connection.

Static validation must prove one first-party production media route and no prohibited upstream
runtime references. Dynamic validation must run the native process integration probe with real
encoded access units before emulator acceptance. Emulator validation remains separate while the
shared ADB runtime is owned by another task.

