# Extraction Map

This file records source provenance and deletion decisions. It is not an implementation
backlog. Gate 3 performed the required source audit before selecting transport and Android
media primitives. Capture, encoder, audio, and presentation work remains governed by that
audit and the later gates.

## Retained Or Adapted Boundaries

| Source | Beacon boundary | Status |
| --- | --- | --- |
| SudoVDA `Common/Include/sudovda-ioctl.h` and `Driver.cpp` | `src/Beacon.Platform.Windows/Displays` | Driver interface, protocol, watchdog-query, and heartbeat IOCTL facts adapted into a Beacon-owned control session; no Apollo runtime or configuration dependency. |
| Apollo, Vibeshine, and Sunshine behavior | `docs/source-audits/2026-07-08-windows-input-sink-upstream-audit.md` | Reference-only audit for display targeting and client-local input ownership; no streaming code copied. |
| Local Steam files | `src/Beacon.Core/Games/Steam`, `src/Beacon.GameProbe` | Read-only parsing of installed applications, libraries, and non-Steam shortcuts. |
| Local Heroic files | `src/Beacon.Core/Games/Heroic` | Read-only parsing of installed GOG and sideloaded applications. |
| Local Hydra database | `src/Beacon.Core/Games/Hydra` | Read-only SQLite query of installed-game rows and executable hints. |
| SteamGridDB | `src/Beacon.Core/Games/SteamGridDb` | API responses only; credentials are supplied at runtime and are not committed. |
| Windows user32, ntdll, and process APIs | `src/Beacon.Platform.Windows/Sessions`, `Input`, and `Recovery` | Native process/window inspection, input injection, and physical recovery boundaries. |
| Android MediaCodec APIs | `src/Beacon.Android/app/src/main/java/dev/beacon/android` | Original Beacon codec inventory, Surface adapter, decoder request, sample, and lifecycle primitives retained for Gate 3 evaluation. |

## Deleted During Architecture Recovery

| Previous source or design | Previous Beacon boundary | Recovery decision |
| --- | --- | --- |
| Sunshine external-process integration | Windows streaming adapter, process runner, manifests, runtime descriptors, probe executable, backend selection, and public handoff metadata | Deleted during Recovery Gate 2. Beacon will not supervise an external streaming product. |
| Sunshine/GameStream compatibility | Server endpoint profiles and Android RTSP/RTP Java stack | Deleted during Recovery Gate 2. No compatibility protocol remains. |
| Moonlight Android and `moonlight-common-c` | Android JNI module, native-session descriptor, native connection, and Git submodules | Deleted during Recovery Gate 2. No upstream client core is packaged. |
| Diagnostic test streaming | Server test assets, HTTP sample routes, Android color bars, encoded-video HTTP route, and alternate client router | Deleted during Recovery Gate 2. Fake-host testing now validates control-plane state only. |
| Android launch handoff | Connection descriptor, launch URI, Android intent fallback, and route selector | Deleted during Recovery Gate 2. StreamCore will own the single future media session boundary. |

The deleted code remains attributable through Git history. It must not be restored as a
shortcut during Gate 3.

## Gate 3 Rule

The selected StreamWorker and StreamCore sources are fixed by
`docs/source-audits/2026-07-10-beacon-streamworker-streamcore.md`. For every copied or
adapted file, implementation must record:

- source repository and immutable revision;
- source file and license;
- copied, adapted, or reference-only decision;
- Beacon destination and ownership boundary;
- why reuse is safer than a Beacon-original implementation;
- tests proving the primitive works through the Beacon-owned contract.

See `docs/license-notes.md` for the repository licensing rule. The source decision does not
change the prohibition against upstream runtime compatibility.

## Gate 3 Selected Primitives

| Source | Beacon destination | License | Reuse decision |
| --- | --- | --- | --- |
| Sunshine `40ae6c8`, `src/platform/windows/display_wgc.cpp` and `display.h` | Future `src/Beacon.StreamWorker/src/capture` | GPL-3.0 | Adapt only monitor selection, free-threaded frame-pool, D3D11 Surface access, and recreation lifecycle into a smaller Beacon-owned WGC boundary. |
| Sunshine `40ae6c8`, `src/platform/windows/display_vram.cpp` | Future `src/Beacon.StreamWorker/src/video` | GPL-3.0 | Reference D3D11-resident texture ownership; implement an original narrow `ID3D11VideoProcessor` BGRA-to-NV12 converter without Sunshine/FFmpeg policy. |
| Sunshine `40ae6c8`, `src/nvenc/nvenc_base.cpp` and `nvenc_d3d11_native.cpp` | Future `src/Beacon.StreamWorker/src/video` | GPL-3.0 | Adapt NVENC resource and cleanup lifecycle; remove Boost, FFmpeg, upstream protocol, codec selection, and policy. |
| microsoft/msquic `v2.5.9` (`87b5308`) | `src/Beacon.StreamProtocol`, `src/Beacon.StreamWorker`, and Android StreamCore native builds | MIT | Linked as the single internal Beacon QUIC implementation; no MsQuic type is exposed above the native transport boundary. |
| MsQuic `src/tools/sample/sample.c` at `87b5308` | `tests/Beacon.StreamProtocol.Tests/msquic_interop_proof.cpp` | MIT | Adapt only callback, configuration, and resource-lifecycle patterns into a Beacon-owned TLS reliable-stream/datagram proof; no sample protocol or product policy is retained. |
| quictls/openssl (`ff36838`, the MsQuic `v2.5.9` gitlink) | Android MsQuic build only | Apache-2.0 | Builds the exact non-Windows TLS source required by pinned MsQuic without initializing or floating an upstream Git submodule. |
| microsoft/xdp-for-windows (`f23b1fb`, the MsQuic `v2.5.9` gitlink) | Windows MsQuic build headers only | MIT | Materialize the exact headers required by pinned MsQuic; XDP remains disabled and absent from Beacon contracts. |
| FFmpeg/nv-codec-headers `15ee327` | Future vendored native include directory | Header-specific permissive notice | Vendor only required NVENC headers and preserve their notice; load the installed NVIDIA runtime library. |
| protocolbuffers/protobuf `v32.1` (`7fcfd66`) | `contracts/worker_ipc.proto`, `contracts/stream_control.proto`, and generated C#/C++ builds | BSD-3-Clause | Generates typed messages from Beacon-owned schemas; no upstream application contract is imported. |
| Sunshine `40ae6c8`, `src/platform/windows/audio.cpp` and `src/audio.cpp`; xiph/opus `v1.6.1` (`22244de`) | Deferred Worker/StreamCore audio boundaries | GPL-3.0 / BSD-3-Clause | Reference/adapt event-driven WASAPI and Opus primitives only after the H.264 gate; no audio code lands in Gate 3. |
| Android `MediaCodec` platform API and retained Beacon codec classes | Future `BeaconStreamCore` Java decoder boundary | Android platform / Beacon GPL-3.0 | Keep and adapt the existing Beacon asynchronous Surface decoder; native transport delivers complete Beacon access units through direct JNI buffers. |

## Gate 3 Implemented Boundaries

- `Beacon.StreamWorker` is a Beacon-owned native process behind versioned, typed named-pipe
  IPC. It emits deterministic access-unit markers for Gate 3 and owns the single MsQuic
  server transport; it does not own display, launch, or session policy.
- Android packages one Beacon-owned JNI StreamCore route with pinned MsQuic and Protobuf
  dependencies. Lifecycle, certificate pinning, ticket handoff, packet assembly, reconnect,
  stop, and exact native-resource release are exercised through that route.
- `contracts/gate3_architecture_guard.json` is the shared prohibited-route manifest consumed
  by both managed and PowerShell validation. It guards source, tests, build files, CI, scripts,
  web assets, and documentation contracts without a compatibility exception ledger.
- The Gate 3 refactor audit added regressions for Android allocator ownership, QUIC send
  context release, Worker process-exit/session correlation, and suspended-process cleanup.
  These repairs preserve the one Worker/StreamCore architecture and add no product route.
