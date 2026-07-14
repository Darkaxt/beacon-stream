# Hosted Emulator Stream Validation

Date: 2026-07-14

## Scope

This checkpoint validates the isolated Beacon wire-to-screen fixture. The test-only Android
endpoint uses the shared server session protocol and media packetizer, while the APK uses its
sole production StreamCore, MediaCodec, and `SurfaceView` path. It does not exercise or package
an alternate runtime, compatibility transport, Windows capture implementation, or display
lifecycle.

## Static And Build Evidence

- `dotnet format`, warning-as-error build, and all 524 managed tests pass.
- `./scripts/build-native-windows.ps1` passes all 26 native tests, including endpoint framing,
  final-feedback/stop ordering, shared authorization, packetization, MsQuic, WGC, D3D11, and
  NVENC regressions.
- The real Worker process probe passes authenticated input, feedback, H.264 access-unit,
  disconnect, shutdown, and benchmark traffic; unsupported live capture fails explicitly.
- `./scripts/test-android.ps1 -Tasks test,assembleDebug,assembleRelease,assembleDebugAndroidTest`
  completes all 117 Gradle tasks while cross-building the endpoint for Android x86_64 and
  arm64-v8a and packaging both app variants plus the instrumentation APK.
- Client Lab lint and all 16 tests pass. The Playwright lifecycle scenario also passes.
- `scripts/test-hosted-emulator-stream.sh` fails at the device boundary before any push when no
  selected emulator is available.
- A fake-ADB process simulation completes certificate generation, coprocess readiness,
  instrumentation dispatch, endpoint evidence, client pixel evidence, and cleanup without
  touching the local ADB server.
- The release APK contains neither hosted endpoint entries nor hosted instrumentation classes.
  Its two `.bau` assets are the intentional production hardware-benchmark vectors.
- Protected source, test, script, and CI paths contain no Apollo, Sunshine, GameStream,
  Moonlight, external-wrapper, descriptor-file, or compatibility-path implementation.

## Hosted Dynamic Evidence

Pending the GitHub Android emulator run. Acceptance requires all of these exact markers:

```text
BEACON_HOSTED_ENDPOINT_AUTHENTICATED 1
BEACON_HOSTED_ENDPOINT_FRAMES 30
BEACON_HOSTED_ENDPOINT_RENDERED_FEEDBACK 30
BEACON_HOSTED_ENDPOINT_STOPPED 1
BEACON_HOSTED_STREAM_FRAMES 30
BEACON_HOSTED_STREAM_PIXEL_VARIANTS <value greater than or equal to 2>
BEACON_HOSTED_EMULATOR_STREAM_OK
```

The validation remains incomplete until GitHub records changing, nonblank `SurfaceView` pixels
from the real H.264 vector and clean endpoint/decoder/Activity teardown.
