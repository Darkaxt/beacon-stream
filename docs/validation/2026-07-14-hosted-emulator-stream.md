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
- `./scripts/build-native-windows.ps1` passes all 27 native tests, including endpoint framing,
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

GitHub Actions run `29356605416` passed on commit `f20a307`. The hosted Android job
executed the APK's sole production StreamCore, MediaCodec, and `SurfaceView` path against the
shared-protocol test endpoint and recorded:

```text
BEACON_HOSTED_ENDPOINT_AUTHENTICATED 1
BEACON_HOSTED_ENDPOINT_FRAMES 30
BEACON_HOSTED_ENDPOINT_RENDERED_FEEDBACK 30
BEACON_HOSTED_ENDPOINT_STOPPED 1
BEACON_HOSTED_STREAM_FRAMES 30
BEACON_HOSTED_STREAM_PIXEL_VARIANTS 30
BEACON_HOSTED_EMULATOR_STREAM_OK
```

This proves changing, nonblank pixels from the real H.264 vector and clean endpoint, decoder,
StreamCore, and Activity teardown. It does not exercise Windows capture, catalog launch,
virtual-display activation, input delivery, reconnect, or physical-primary restoration; those
remain part of the full Gate 5 dynamic acceptance.

## Refactor Audit

- Production Worker and the hosted endpoint both consume the shared session protocol, ticket
  authorizer, and media packetizer; no second wire contract or parser was retained.
- Hosted endpoint and instrumentation symbols are absent from production Android, Worker, and
  Server source sets and from the release APK.
- New lifecycle waits are condition/latch driven. MsQuic idle timeout remains disabled rather
  than owning cancellation or session teardown.
- StreamCore sends the terminal `StopSession` on the reliable session stream before transport
  release, and the server closes only after accepted stop and final rendered-frame feedback.
- The post-acceptance audit found no additional ownership or duplication refactor justified by
  current evidence. Full product transaction work remains explicitly separate.
