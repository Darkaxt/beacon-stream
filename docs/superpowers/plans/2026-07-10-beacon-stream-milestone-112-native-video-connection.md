# Beacon Stream Milestone 112: Native Video Connection

## Goal

Route a validated server-provisioned native session through `moonlight-common-c` and render its video decode units on Beacon's existing Android `SurfaceView` with `MediaCodec`.

## Boundary

- `MoonlightNativeConnection` owns one native connection lifecycle and calls `LiStartConnection`, `LiInterruptConnection`, and `LiStopConnection`.
- Beacon-owned JNI maps `MoonlightNativeSessionPlan` fields into `SERVER_INFORMATION` and `STREAM_CONFIGURATION` without choosing different policy values.
- Connection and video callbacks cross JNI through small policy-free Java interfaces.
- The APK video renderer feeds decode units into a bounded blocking sample queue consumed by the existing asynchronous `MediaCodec` adapter.
- A native session takes precedence over the legacy partial Java RTSP/RTP route. Descriptors without a native session keep the existing fallback behavior.

## Lifecycle Rules

- A replacement start stops the previously active native connection first.
- A start in progress can be interrupted without marking the core idle before `LiStartConnection` returns.
- Stop does not use a timeout, sleep, retry loop, or polling watchdog.
- JNI global references remain alive until start failure or `LiStopConnection` completes.
- Decoder cleanup releases queue, codec, and surface-owned state deterministically.

## Video Rules

- H.264, HEVC, and AV1 native format masks map to Android codec names without changing the server-selected format.
- Parameter-set buffers and picture data remain separate decode submissions, matching `moonlight-common-c` callback semantics.
- The queue has bounded sample capacity and uses blocking backpressure rather than timed polling or frame-loss guesses.
- Native presentation timestamps are passed through to `MediaCodec`.
- Audio, controller, touch, mouse, keyboard, and HDR display-mode switching remain later callbacks; the core may discard audio until its renderer is implemented.

## Test-First Sequence

1. Add host-JVM lifecycle tests around injectable native bindings.
2. Add queue and MediaCodec-renderer lifecycle tests with fake codecs and surfaces.
3. Add native-session route tests proving valid descriptors start the native path, invalid descriptors report their validation error, and stop releases the native connection.
4. Implement JNI plan marshaling and connection/video callbacks.
5. Compile all Android ABIs and invoke the native library on the emulator.

## Validation And Sync

1. Focused Android library and app JVM tests.
2. Full `.NET`, Android, Client Lab, and Playwright validation.
3. Emulator instrumentation against `emulator-5554` only.
4. Commit, push, PR, CI, and merge.
5. Review callback ownership and duplicated lifecycle state.
6. Refactor if justified, re-run static/dynamic validation, and sync again.

## Exit Evidence

- A valid `MoonlightNativeSessionPlan` can reach the real `LiStartConnection` entry point.
- Native connection stages and termination are observable in Java.
- Native video setup and decode units reach a Surface-backed MediaCodec renderer.
- Stop and failed start release native and decoder state without a timeout.
- The APK selects this path only when the server provides a valid native session.
- Documentation does not claim a live Apollo stream until server-owned pairing and `/launch` provisioning are complete.

## Implementation Evidence

- JVM tests cover native connection state, strict native-session routing, fallback precedence, bounded queue shutdown, codec-family mapping, timestamp/data forwarding, failure diagnostics, and cleanup.
- The Android build compiles the JNI bridge and `moonlight-common-c` for `arm64-v8a`, `armeabi-v7a`, `x86`, and `x86_64`.
- Emulator instrumentation on `emulator-5554` loads the native library, resolves the `LiStartConnection` binding, maps the owning-client response, and configures a real H.264 `MediaCodec` against an Android `Surface`.
- Live Apollo streaming remains the next server-provisioning milestone; this implementation does not synthesize pairing credentials or claim end-to-end host video yet.
