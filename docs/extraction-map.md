# Extraction Map

This file records source provenance and deletion decisions. It is not an implementation
backlog. Gate 3 must perform a new source audit before selecting any capture, encoder,
audio, transport, or Android media implementation.

## Retained Or Adapted Boundaries

| Source | Beacon boundary | Status |
| --- | --- | --- |
| Apollo `third-party/sudovda/sudovda-ioctl.h` | `src/Beacon.Platform.Windows/Displays/WindowsDisplayApi.cs` | Protocol facts adapted for the SudoVDA interface GUID, protocol version, and IOCTL constants. |
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

No StreamWorker or StreamCore source has been selected. The Gate 3 audit must compare
mature implementations at the primitive level and record, for every copied or adapted file:

- source repository and immutable revision;
- source file and license;
- copied, adapted, or reference-only decision;
- Beacon destination and ownership boundary;
- why reuse is safer than a Beacon-original implementation;
- tests proving the primitive works through the Beacon-owned contract.

See `docs/license-notes.md` for the repository licensing rule.

The completed Gate 3 source decision is recorded in
`docs/source-audits/2026-07-10-beacon-streamworker-streamcore.md`. That audit supersedes
the temporary "no source selected" state above without changing the prohibition against
upstream runtime compatibility.

## Gate 3 Selected Primitives

| Source | Beacon destination | License | Reuse decision |
| --- | --- | --- | --- |
| Sunshine `40ae6c8`, `src/platform/windows/display_wgc.cpp` and `display.h` | Future `src/Beacon.StreamWorker/src/capture` | GPL-3.0 | Adapt only monitor selection, free-threaded frame-pool, D3D11 Surface access, and recreation lifecycle into a smaller Beacon-owned WGC boundary. |
| Sunshine `40ae6c8`, `src/platform/windows/display_vram.cpp` | Future `src/Beacon.StreamWorker/src/video` | GPL-3.0 | Reference D3D11-resident texture ownership; implement an original narrow `ID3D11VideoProcessor` BGRA-to-NV12 converter without Sunshine/FFmpeg policy. |
| Sunshine `40ae6c8`, `src/nvenc/nvenc_base.cpp` and `nvenc_d3d11_native.cpp` | Future `src/Beacon.StreamWorker/src/video` | GPL-3.0 | Adapt NVENC resource and cleanup lifecycle; remove Boost, FFmpeg, upstream protocol, codec selection, and policy. |
| microsoft/msquic `v2.5.9` (`87b5308`) | Future native StreamProtocol, StreamWorker, and Android StreamCore builds | MIT | Link the library as the single internal Beacon QUIC implementation; expose no MsQuic type above the native transport boundary. |
| FFmpeg/nv-codec-headers `15ee327` | Future vendored native include directory | Header-specific permissive notice | Vendor only required NVENC headers and preserve their notice; load the installed NVIDIA runtime library. |
| protocolbuffers/protobuf `v32.1` (`7fcfd66`) | Future generated Worker IPC and stream-control contracts | BSD-3-Clause | Generate typed C#/C++ messages from Beacon-owned schemas; no upstream application contract is imported. |
| Sunshine `40ae6c8`, `src/platform/windows/audio.cpp` and `src/audio.cpp`; xiph/opus `v1.6.1` (`22244de`) | Deferred Worker/StreamCore audio boundaries | GPL-3.0 / BSD-3-Clause | Reference/adapt event-driven WASAPI and Opus primitives only after the H.264 gate; no audio code lands in Gate 3. |
| Android `MediaCodec` platform API and retained Beacon codec classes | Future `BeaconStreamCore` Java decoder boundary | Android platform / Beacon GPL-3.0 | Keep and adapt the existing Beacon asynchronous Surface decoder; native transport delivers complete Beacon access units through direct JNI buffers. |
