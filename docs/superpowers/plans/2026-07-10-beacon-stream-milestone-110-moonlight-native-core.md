# Beacon Stream Milestone 110: Moonlight Native Core

## Goal

Replace the long-term foundation of Beacon's partial handwritten GameStream client with a headless, policy-free Android native module built from the mature Moonlight transport core. Prove the module is reproducible and loadable on the Android emulator before routing real sessions through it.

## Source Decision

- Use `moonlight-stream/moonlight-common-c` as the transport, crypto, media, and input protocol implementation.
- Pin the imported source to upstream commit `2ea4775` (2026-07-05) and record its GPL-3.0 provenance.
- Use the JNI and Android renderer boundaries in `moonlight-stream/moonlight-android` and `ClassicOldSong/moonlight-android` only as extraction references. Do not import either product UI, settings model, discovery flow, or launch architecture.
- Keep Beacon Server authoritative. A later milestone will provision a complete native session descriptor; the APK must not independently reinterpret display or stream policy.

## Scope

- Add a dedicated Android library module for the Moonlight native core.
- Import only the JNI/native dependencies and minimal Java contracts required to build and load the core.
- Keep copied/adapted source provenance explicit in the extraction map and file headers where appropriate.
- Add a small native availability API that proves the JNI bridge and pinned Moonlight core are loaded.
- Add an emulator instrumentation test for the native availability API.
- Expose native-core readiness to the Beacon APK without routing production GameStream sessions through an incomplete descriptor.
- Preserve the existing diagnostic and Java GameStream paths until the native replacement has equivalent session inputs and lifecycle coverage.

## Out Of Scope

- Apollo/Sunshine pairing or launch control.
- Server-provisioned GameStream session keys and RTSP session URLs.
- Replacing the active GameStream route.
- Audio/video renderer migration.
- Native input migration.
- HDR activation.

These are subsequent milestones on the same objective, not optional follow-up work.

## Test-First Sequence

1. Add a failing instrumentation test that requires the native module to report a stable core identity and stage name.
2. Add the Android library module and native bridge needed to satisfy the test.
3. Build all supported APK ABIs and run the instrumentation test on `emulator-5554`.
4. Add a JVM contract test proving native readiness is reported truthfully when the bridge is available or unavailable.
5. Run the existing Android JVM suite to verify the Java GameStream route is unchanged.

## Validation And Sync

1. Focused native-module build and tests.
2. Full Android `test assembleDebug`.
3. Emulator install, cold process start, native bridge invocation, and instrumentation result.
4. Full `.NET` solution tests and `git diff --check`.
5. Update README and extraction map with the exact boundary and remaining work.
6. Commit, push, open a pull request, wait for CI, and merge.
7. Review the landed milestone for useful simplification or duplication removal.
8. If justified, refactor, repeat static and emulator validation, and sync a second checkpoint.

## Exit Evidence

- The repository contains a pinned, license-documented Moonlight native-core source boundary.
- The Beacon Android build produces the native library for the emulator and device ABIs.
- An emulator instrumentation test loads the library and invokes Moonlight core code successfully.
- No existing route claims real native streaming before the server can supply a complete native session descriptor.
