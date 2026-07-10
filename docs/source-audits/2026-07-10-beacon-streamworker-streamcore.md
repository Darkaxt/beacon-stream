# Beacon StreamWorker And StreamCore Source Audit

**Date:** 2026-07-10
**Status:** approved Gate 3 source decision
**Scope:** Beacon-owned Windows capture/encode worker, private media transport, Android StreamCore, audio follow-up, and input integration

## Decision Summary

Beacon will implement one native C++20 `Beacon.StreamWorker` and one APK-owned
`StreamCore`. Beacon Service remains the .NET authority for identity, policy, display
leases, games, session ownership, and recovery. StreamWorker owns capture, GPU conversion,
encoding, media transport, and media-session resources. StreamCore owns the authenticated
QUIC session, frame assembly, decoder feed, rendering, feedback, and client input transport.

There is no Apollo, Sunshine, GameStream, Moonlight, RTSP, RTP, wrapper, descriptor-file,
launch-URI, or selectable transport contract. Upstream projects are source evidence only.

The selected first production path is:

```text
Windows.Graphics.Capture monitor event
-> ID3D11Texture2D BGRA
-> D3D11 video processor scale/convert
-> ID3D11Texture2D NV12
-> native NVENC H.264 low-latency access unit
-> Beacon media datagrams over MsQuic
-> bounded StreamCore frame assembly
-> Android MediaCodec asynchronous Surface decode
```

## Local Target Facts

The source decision is intentionally optimized for the personal target while retaining
narrow internal interfaces:

- Windows 11 with Windows SDK `10.0.26100.0` and `10.0.22621.0` available.
- NVIDIA GeForce RTX 4090 Laptop GPU with driver `610.47` and Intel UHD Graphics.
- SudoVDA provides the per-client virtual monitor selected by Beacon Service.
- The current Java-only APK floor is API 26. Pinned MsQuic builds quictls for Android API 29,
  so StreamCore raises the production APK floor to API 29 when native transport lands. The
  primary device is a Z Fold 7.
- FFmpeg 8.0.1 is installed locally, but Beacon will not ship or execute FFmpeg for the
  first path. Its backend breadth and GPL distribution surface are unnecessary here.

An unsupported capability fails preflight with evidence. It does not silently select a
different capture, encoder, transport, or display.

## Pinned Evidence And Dependencies

| Source | Immutable revision | License | Decision |
| --- | --- | --- | --- |
| LizardByte/Sunshine | `40ae6c800274afff664838cc48386e01fcffe2cd` | GPL-3.0 | Reference and selectively adapt primitive lifecycle/error handling; never retain Sunshine policy or protocol. |
| microsoft/msquic `v2.5.9` | `87b53085d76bd7920d490a6f226c9999b6614d14` | MIT | Build and package as the single QUIC implementation on Windows and Android. |
| FFmpeg/nv-codec-headers | `15ee32753c92faddbabbff11676779618fc6db7e` | permissive header notice | Vendor the required NVENC API headers with notice; load `nvEncodeAPI64.dll` at runtime. |
| protocolbuffers/protobuf `v32.1` | `7fcfd66022455635fa29af92987cdc0967efd4f3` | BSD-3-Clause | Generate typed Worker IPC and reliable stream-control messages from Beacon-owned schemas. |
| quictls/openssl (MsQuic `v2.5.9` gitlink) | `ff36838bb69801cad56823159a036977bcbe5c75` | Apache-2.0 | Build MsQuic's non-Windows TLS dependency at its audited gitlink revision for Android; never fetch a floating submodule. |
| microsoft/xdp-for-windows (MsQuic `v2.5.9` gitlink) | `f23b1fb4d492d9c20bcd7767bba2278f94355df8` | MIT | Materialize headers required by the pinned Windows MsQuic platform build; Beacon does not enable or expose XDP. |
| xiph/opus `v1.6.1` | `22244de5a79bd1d6d623c32e72bf1954b56235be` | BSD-3-Clause | Deferred audio encoder/decoder dependency after the H.264 gate. |

Dependencies are fetched or vendored at exact revisions into ordinary directories. No Git
submodules, symlinks, floating branches, runtime downloads, or upstream executables are
allowed. Dependency notices are added before copied/adapted code lands.

## Windows Capture

**Choice:** Windows Graphics Capture (WGC), selected by monitor, with a free-threaded D3D11
frame pool and `FrameArrived` callback.

Primary evidence:

- Sunshine `src/platform/windows/display_wgc.cpp` demonstrates `CreateForMonitor`,
  `CreateFreeThreaded`, `FrameArrived`, high-refresh `MinUpdateInterval`, Surface-to-D3D11
  texture access, and capture-pool recreation.
- Microsoft documents WGC as a display/window frame source and identifies
  `SystemRelativeTime` as QPC time suitable for media synchronization:
  <https://learn.microsoft.com/windows/apps/develop/media-authoring-processing/screen-capture>

Beacon will write a smaller `WgcDisplayCapture` around the selected monitor identity. It
will create the D3D11 device on the NVIDIA adapter, publish texture ownership through a
bounded frame queue, and recreate the frame pool when the content size/device changes.
Capture callbacks signal state transitions; no polling sleep or capture timeout owns the
lifecycle.

Desktop Duplication in Sunshine `src/platform/windows/display_base.cpp` remains diagnostic
evidence only. It is not a fallback because `AcquireNextFrame` polling, adapter/output
coupling, and fallback races add a second production path.

## GPU Conversion And Scaling

**Choice:** `ID3D11VideoProcessor` on the same D3D11 device, BGRA input to NV12 output,
with source/destination rectangles derived from the immutable plan.

Beacon will query `ID3D11VideoProcessorEnumerator` support for the exact input/output
formats and dimensions during preflight, then use `VideoProcessorBlt` for scaling and color
conversion. The output texture is registered directly with NVENC. Unsupported conversion
fails closed; a shader fallback is not added to version one.

Primary evidence:

- Sunshine `src/platform/windows/display_vram.cpp` proves a D3D11-resident capture and
  conversion pipeline and identifies the NV12/P010 texture boundary.
- Microsoft documents the D3D11 video processor and `VideoProcessorBlt` surface operation:
  <https://learn.microsoft.com/windows/win32/api/d3d11/nn-d3d11-id3d11videodevice>
  and
  <https://learn.microsoft.com/windows/win32/api/d3d11/nf-d3d11-id3d11videocontext-videoprocessorblt>.

SDR uses BT.709 limited-range metadata for the first vertical slice. HDR conversion is not
claimed from this path. A later HDR gate must prove WGC float capture, P010 conversion,
encoder metadata, protocol metadata, decoder, and panel presentation together.

## H.264 Encoder First

**Choice:** direct NVENC C API over a D3D11 NV12 texture. No FFmpeg process or libavcodec
adapter is part of the first production path.

Beacon will adapt only the proven resource lifecycle visible in Sunshine
`src/nvenc/nvenc_base.cpp` and `src/nvenc/nvenc_d3d11_native.cpp`: load API, query
capabilities, open DirectX session, register/map texture, submit picture, lock/copy/unlock
bitstream, unmap/unregister, and destroy exactly once. Beacon-owned code removes Boost,
FFmpeg frames, Sunshine configuration, codec negotiation, and upstream recovery policy.

The first encoder contract is fixed to H.264 SDR, NV12, no B frames, low-latency rate
control, repeated SPS/PPS on every forced IDR, and monotonic frame/QPC timestamps. Initial
bitrate and FPS come only from the immutable server plan. Congestion evidence may call
`NvEncReconfigureEncoder` for bitrate within the same planned mode. Resolution or format
changes require an explicit server replan.

NVIDIA's programming guide documents D3D11 resource registration, forced IDR/SPS-PPS, and
in-place bitrate reconfiguration:
<https://docs.nvidia.com/video-technologies/video-codec-sdk/13.1/nvenc-video-encoder-api-prog-guide/index.html>.

## Audio Capture And Encode

**Choice after the H.264 gate:** event-driven shared-mode WASAPI loopback at the selected
render endpoint, converted to 48 kHz float PCM and encoded with Opus.

Sunshine `src/platform/windows/audio.cpp` and `src/audio.cpp` are reference/adaptation
sources for endpoint discovery, `AUDCLNT_STREAMFLAGS_LOOPBACK | EVENTCALLBACK`, channel
mapping, and Opus packetization. Microsoft confirms event-driven loopback support on modern
Windows:
<https://learn.microsoft.com/windows/win32/coreaudio/loopback-recording>.

Audio uses the same session epoch and QPC-derived presentation timeline as video. Each Opus
packet is one media datagram. StreamCore uses libopus decode and Android AAudio output.
Loss invokes Opus packet-loss concealment; audio packets are never retransmitted. Audio is
not implemented until video-only emulator streaming, stop, and reconnect pass.

## Authenticated Transport

**Choice:** MsQuic on Windows and Android using one IETF QUIC connection and ALPN
`beacon-stream/1`.

Why:

- QUIC provides TLS 1.3 authentication/encryption, reliable independent streams, unreliable
  datagrams, congestion control, pacing, path MTU evidence, and connection statistics in one
  library.
- RFC 9221 explicitly targets real-time media and applies QUIC congestion control to
  unreliable datagrams: <https://www.rfc-editor.org/rfc/rfc9221.html>.
- MsQuic documents independent reliable streams and datagram state/maximum-send-length:
  <https://microsoft.github.io/msquic/msquicdocs/docs/Streams.html> and
  <https://microsoft.github.io/msquic/msquicdocs/docs/api/QUIC_CONNECTION_EVENT.html>.

The first Gate 3 task must compile pinned MsQuic for Windows x64 and Android arm64/x86_64,
then prove encrypted stream and datagram interoperability between host and emulator. The
Windows build uses MSVC/Schannel. The Android cross-build runs under Linux (CI and local WSL)
with the Linux Android NDK and the pinned quictls source because MsQuic's own build tooling
rejects Android cross-builds from a Windows host. MsQuic lists Android as best-effort, so this
proof is a blocking foundation check, not an assumption buried beneath product code.

Beacon persists one server identity certificate. The paired APK pins its public-key
fingerprint. The authenticated control plane issues an opaque random session ticket; Beacon
Service provisions only its SHA-256 hash, client/session binding, plan revision, and security
expiry to StreamWorker. The binding also includes the current StreamWorker instance id so a
ticket cannot survive a Worker replacement. StreamCore sends the raw ticket after TLS
validation. StreamWorker compares in constant time and consumes the record before accepting
media or input. Reconnect requests a new ticket for the same server-owned session.

Initial registration is not authorized merely by knowing a shared token. The APK presents a
stable client identity over pinned HTTPS, Beacon records a pending registration, and the
trusted local Cockpit explicitly approves it before Service issues the per-client credential.
Owning-client APIs validate that credential and scope every action to that client's session;
broader recovery remains local Cockpit authority.

QUIC idle expiry is not used to terminate application/display ownership. Explicit stop,
connection-close events, worker process exit, and server state transitions own cleanup.
Heartbeats may measure liveness but cannot cancel a session.

## Service To StreamWorker IPC

**Choice:** one local duplex Windows named pipe using overlapped/event-driven I/O, owner-only
ACL, 32-bit network-order length framing, and Protobuf messages from
`contracts/worker_ipc.proto`.

Beacon Service launches one persistent StreamWorker in the interactive user session. The
pipe name contains a cryptographically random instance suffix and permits only the owning
user and LocalSystem. A Windows Job Object closes the Worker if the Service process exits;
stream disconnect does not kill it. Worker process exit is observed through its process
handle, not a polling watchdog.

The message families are:

- `WorkerHello`, `WorkerReady`, `WorkerCapabilities`, and `WorkerHealth`;
- `AuthorizeTicket` and `RevokeTicket`;
- `PrepareSession`, `StartMedia`, `StopMedia`, and `RequestIdr`;
- `SessionStateChanged`, `MediaMetrics`, and structured `WorkerDiagnostic`;
- `ShutdownWorker` for explicit service shutdown.

Every command carries protocol version, request id, and session id where applicable. Every
request receives exactly one completion or a process-exit failure. Message sizes are bounded
before allocation. Paths, raw tickets, certificate private material, and input contents never
appear in diagnostics.

## Worker To StreamCore Contract

Reliable stream messages use `contracts/stream_control.proto`, each prefixed by a 32-bit
network-order size. One QUIC connection contains:

- one client-initiated bidirectional session stream for ticket authentication, negotiated
  facts, lifecycle, and explicit shutdown;
- one client-initiated unidirectional input stream for ordered input batches;
- one client-initiated unidirectional feedback stream for rendered-frame, loss, decoder,
  queue-depth, and benchmark evidence;
- congestion-controlled QUIC datagrams for video and, later, audio.

The public session envelope contains only protocol version, server address/port, raw
single-use ticket, pinned server fingerprint, selected codec/resolution/FPS/SDR facts, and
plan revision/explanation. It contains no executable path, policy switch, backend name,
launch URI, wrapper field, or long-lived secret.

### Media Datagram V1

Each datagram begins with one fixed 40-byte network-order Beacon header:

```text
magic:u32 | version:u8 | media_kind:u8 | flags:u16
sequence:u64 | presentation_time_us:u64 | frame_bytes:u32
chunk_index:u16 | chunk_count:u16 | payload_offset:u32
payload_bytes:u16 | reserved:u16
```

Payload size is derived from MsQuic's negotiated `MaxSendLength`; it is not a hard-coded IP
MTU. One H.264 access unit owns one sequence and is split across chunks. Flags identify IDR,
codec configuration, and end-of-access-unit facts. QUIC already provides integrity and
encryption, so Beacon adds no checksum.

StreamCore accepts at most four incomplete video frames and bounds each frame by the planned
dimensions and an absolute 16 MiB ceiling. Duplicate, overlapping, out-of-range, or
inconsistent chunks are rejected. When sequence progress supersedes capacity, the oldest
incomplete frame is discarded, the decoder enters `awaiting_idr`, and reliable feedback asks
StreamWorker for a forced IDR. Non-IDR frames are discarded until a complete IDR arrives.
This is sequence/capacity driven; no reassembly timeout exists.

MsQuic owns packet congestion and pacing. Beacon's rate controller consumes QUIC loss/RTT/
congestion-window facts plus StreamCore queue/drop/render feedback and may reduce or increase
NVENC bitrate within server-plan bounds. Video datagrams are never retransmitted. FEC is not
part of the first protocol; it can only be added later from benchmark evidence under the same
media contract.

## Android Decoder And Render

**Choice:** native C++ MsQuic transport and frame assembler behind one Java
`BeaconStreamCore`, feeding the existing Beacon-owned asynchronous `MediaCodec` Surface
adapter through direct JNI buffers on a dedicated decoder thread.

The existing `AndroidMediaCodecCatalog`, capability probe, `AndroidMediaCodecFactory`,
`AndroidSurfaceViewProvider`, and `SurfaceEncodedVideoDecoder` remain the adaptation base.
`MediaCodec` is configured for `video/avc`, the planned dimensions, Surface output, and
low-latency mode when the selected codec reports support. Android documents asynchronous
callbacks, Surface decode, and `KEY_LOW_LATENCY`:
<https://developer.android.com/reference/android/media/MediaCodec>.

Transport callbacks never run codec work or UI work inline. A bounded decoder queue receives
complete access units. Render feedback uses presentation timestamp and sequence. Stop is an
idempotent state transition that closes QUIC, assembler, codec, Surface ownership, input,
and future audio exactly once.

The old Java and server Annex-B splitters are deleted when the access-unit contract lands;
StreamWorker already emits complete access units and StreamCore reconstructs them directly.

## Input Transport And Injection

StreamCore maps local touch/controller/keyboard state to versioned Beacon input batches and
writes them to the dedicated reliable input stream. StreamWorker validates session identity,
sequence monotonicity, event count, normalized coordinates, and permissions, then forwards
typed batches over Worker IPC. Beacon Service remains the authority that targets the leased
display and calls the existing `WindowsClientInputSink`/`WindowsInputApi`.

This keeps input in the same authenticated session without moving display policy or Windows
injection into native media code. Large video frames cannot block input because video uses
datagrams and input has its own QUIC stream.

## Testing Without A Phone

The permanent test ladder is:

1. C++ protocol tests with golden Protobuf frames and media-header vectors.
2. Deterministic in-memory transport with programmable packet drop/reorder/duplication by
   sequence, never by wall-clock delay.
3. Real child-process/named-pipe Worker integration with fake capture and fake encoder.
4. Real MsQuic loopback and Android-emulator interop using test certificates/tickets.
5. Client Lab control-plane simulation and existing .NET/Playwright suites.
6. Real WGC/D3D11/NVENC H.264 from the planned virtual display to MediaCodec on the emulator.
7. Physical Z Fold 7 only after the emulator gate, for 120 Hz, Wi-Fi, thermals, input, audio,
   and HDR confirmation.

The emulator harness must recover `emulator-5554` and avoid repeated `uiautomator` hierarchy
polling that previously ANRed Android's system process. Test completion is signaled by
Beacon/StreamCore state and structured diagnostics.

## Explicit Rejections

- No Apollo/Sunshine process, config, pairing, app list, or protocol.
- No GameStream, Moonlight, RTSP, RTP, HTTP media, WebRTC, browser client, or launch intent.
- No FFmpeg executable or selectable capture/encoder/transport backend.
- No Desktop Duplication or software encoder fallback in version one.
- No media retransmission, FEC, HDR, HEVC, AV1, or audio before the H.264 emulator gate.
- No descriptor files, startup sleeps, polling readiness, cancellation timeout, or lifecycle
  watchdog.
- No client-side stream/display policy. The APK reports facts and selects a catalog item.

## Exit Decision

The repository may proceed to Gate 3 using only these boundaries. Any source substitution,
new fallback, or compatibility route requires an updated audit and architecture test before
implementation.

## Gate 3 Platform Proof Evidence

The first Gate 3 implementation slice validated the transport choice rather than assuming it:

- Visual Studio 17.14/MSVC 14.44 built pinned MsQuic `v2.5.9` with Schannel as a shared
  library; Beacon's API lifecycle test loaded the adjacent built DLL and passed.
- Google NDK r27d (`27.3.13750724`) was installed under WSL from the published Linux archive
  after verifying SHA-1 `22105e410cf29afcf163760cc95522b9fb981121`.
- WSL built the same StreamProtocol graph and pinned quictls dependency for Android x86_64
  and arm64-v8a at API 29 with Beacon code under warnings-as-errors.
- The x86_64 API lifecycle executable ran successfully on `emulator-5554`; arm64 output was
  verified as an AArch64 Android ELF for the physical target.
- `test-msquic-emulator-interop.ps1` created a temporary Schannel certificate, waited on the
  server readiness event, connected the Android client through `10.0.2.2`, and proved one TLS
  QUIC reliable-stream payload plus one QUIC datagram payload. Both peers reported success,
  and the temporary certificate was removed.

No product session, ticket, media packet, fallback transport, or APK native route is added by
this proof. Those remain test-first work in the following Gate 3 slices.
