# Android Decoder Ownership Audit

Date: 2026-07-14

## Scope

This partial Gate 5 refactor audit covers the APK MediaCodec output contract and the Java
decoder lifecycle boundary. It does not claim emulator rendering acceptance or complete Task 21.

## Defect And Regression

`AndroidMediaCodecFactory.QueueingCallback` treated every output callback without a tracked
Beacon frame identity as a decoder failure. Codec-configuration buffers and empty end-of-stream
buffers do not represent rendered frames and therefore have no frame identity. Reporting them as
errors could contaminate benchmark evidence and trigger unnecessary recovery during surface or
decoder reset.

The first JVM regression failed to compile because production had no output classification
boundary. The implementation now requires identity only for a nonempty, non-codec-configuration
payload. A final frame carrying the end-of-stream flag remains renderable and still requires its
tracked identity.

## Ownership Reduction

The audit also proved that `EncodedVideoDecoder` had no typed consumer. The unused interface was
deleted, `SurfaceEncodedVideoDecoder` remains the sole decoder lifecycle owner, and an architecture
test prevents the parallel generic owner from returning.

## Validation

- Focused MediaCodec classification, Surface decoder, and video-pipeline JVM tests: passed.
- Clean Android `test`, debug/release assembly, and instrumentation APK assembly: 118 tasks
  passed; debug and release each ran 151 JVM tests with zero failures, errors, or skips.
- Android native StreamCore built for `x86_64` and `arm64-v8a`.
- .NET formatting and warning-as-error build: passed with zero warnings.
- Full managed solution: 512 tests passed.
- Windows native rebuild and CTest: 23/23 passed.
- Production Worker process proof: authenticated H.264 video, input/feedback, reliable and
  datagram benchmark traffic, RTT, disconnect, shutdown, and invalid startup all passed.
- Client Lab lint and 16 tests: passed.
- Client Lab Playwright lifecycle: passed.
- Architecture absence, secret fixtures, Kestrel parser fixtures, and `git diff --check`: passed.
- The forbidden runtime/test reference scan remained empty.

No ADB command, emulator action, display-topology mutation, or external streaming installation
interaction occurred during this validation.

## Remaining

Structured emulator first-frame and moving-frame evidence remains open. The broader Gate 5 audit
must still cover native frame/buffer cleanup, JNI/Surface lifecycle under a real process,
reconnect compensation, diagnostic truth, and complete dynamic recovery acceptance.
