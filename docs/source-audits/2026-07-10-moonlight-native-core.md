# Moonlight Native Core Source Audit

## Decision

Beacon will use `moonlight-stream/moonlight-common-c` as its Android GameStream transport core instead of completing the partial Java RTSP/RTP implementation as a bespoke protocol stack.

Beacon remains responsible for client identity, profile ownership, display leases, launch policy, session planning, diagnostics, and recovery. The native core remains headless and policy-free. A later milestone will give it a complete server-provisioned session descriptor before the active GameStream route is migrated.

## Sources Reviewed

| Source | Revision | Use |
| --- | --- | --- |
| `moonlight-stream/moonlight-common-c` | `2ea47752c3051d72a64bcca190024e8b354fa1ef` | Included as a recursive GPL-3.0 submodule for mature GameStream transport, crypto, media packet, and input protocol behavior. |
| `moonlight-stream/moonlight-android` | `f10085f552b367cf7203007693d91c322a0a2936` | JNI, MediaCodec, audio, and connection composition reference only. No product UI or settings code imported. |
| `ClassicOldSong/moonlight-android` | `3397ec7750969466ad8983364ee1a33182bbffa1` | Artemis compatibility and Android integration reference only. No product UI or settings code imported. |
| `Mbed-TLS/mbedtls` | `068ff080b369adfac81509f9b57b2afabaf82dc5` (`mbedtls-3.6.7`) | Included as a recursive Apache-2.0/GPL-compatible submodule for the native core crypto implementation. |
| `LizardByte/Sunshine` and `ClassicOldSong/Apollo` | Local source snapshots reviewed 2026-07-10 | Host capture/encode and compatibility references. They remain separate from this client-core extraction. |

## Why This Boundary

The existing Beacon Java path already proves server descriptors, RTSP sequencing, RTP socket ownership, H.264 depacketization, and MediaCodec handoff. It does not yet implement the complete production protocol: encrypted media, FEC, audio/control processing, native input, congestion behavior, and mature session recovery are still missing.

`moonlight-common-c` already owns those protocol concerns and continues to receive upstream fixes. Keeping a parallel handwritten implementation would increase protocol drift and contradict Beacon's requirement to build from proven streaming components.

## Imported Boundary

- `src/Beacon.Android/streaming-moonlight` is a dedicated Android library.
- The module builds the pinned native core with Mbed TLS and exposes only a small Beacon-owned JNI availability surface in Milestone 110.
- The module does not contain Beacon profiles, display policy, game collection behavior, or UI settings.
- The APK reports whether the native library loaded through its existing decoder diagnostics.
- Production GameStream routing remains unchanged until the server can provide every native connection input explicitly.

## License And Update Policy

Beacon Stream is GPL-3.0, matching `moonlight-common-c`. Mbed TLS 3.6.7 is Apache-2.0 and GPL-compatible. Recursive submodule revisions are pinned in Git; updates require a focused source audit, static validation, emulator instrumentation, and a synchronized extraction-map update.
