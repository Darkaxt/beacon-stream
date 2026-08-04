# Android MediaCodec StreamCore Validation

Date: 2026-07-14

## Implemented Boundary

- StreamCore delivers generation-scoped, Java-owned direct access units with IDR and codec
  configuration metadata.
- A bounded three-access-unit queue owns decoder admission and recovery.
- MediaCodec configuration and lifecycle run on a serial decoder executor.
- Render feedback comes from the frame-rendered callback and retains sequence plus PTS.
- Surface loss, decoder failure, stop, reconnect, and close reject stale callbacks and release
  each codec instance once.
- Benchmark vectors use a complete-access-unit container; the Android and server Annex-B
  splitters and the server sample envelope are removed.
- Production Activity and ViewModel ownership bind the selected video grant to the Surface
  pipeline and compensate failed starts and failed cleanup.

## Static And Simulated Evidence

- Android focused red/green tests covered decoder replacement, failed video start compensation,
  creation cleanup, and native release after video cleanup failure.
- Android unit tests, debug APK assembly, and instrumentation-source compilation passed.
- Android native x86_64 and arm64 builds passed before the Java-only cleanup refactor.
- Windows native build and all 22 CTest cases passed.
- StreamWorker process integration reported authenticated input, feedback, access-unit delivery,
  disconnect, shutdown, and expected invalid-startup behavior.
- The .NET format/build gates passed with zero warnings or errors; 487 tests passed.
- Gate 3 prohibited-route, secret-fixture, and runner-parser checks passed.

## Remaining Dynamic Evidence

The emulator first-frame, moving-frame, Surface instrumentation, and reconnect evidence is not
claimed. Another task currently owns ADB and the emulator, so this validation did not issue ADB
commands or inspect that runtime. No external streaming service, display driver, or Windows
display topology was queried or changed.
