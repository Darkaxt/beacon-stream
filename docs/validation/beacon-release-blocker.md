# Beacon Release Blocker

Updated: 2026-08-02

## Last Complete Outcome

R1 Integrated Streaming Proof is complete. The guarded production runner passed one retained,
emulator-backed transaction from physical-primary baseline through production streaming and back to
verified physical-only topology.

## Last Verified Transaction

- `.artifacts/gate5-production-b6bd2b1e73a946d49e5195e613d4da96/` contains the prepared, active,
  and restored server snapshots, Android instrumentation log, SessionProbe evidence, Server output,
  and independent display-guard log.
- The prepared snapshot shows `DISPLAY1` physical-primary at `2560x1600@240`, the per-client virtual
  display extended at `2560x1600@120`, and mirror mode disabled.
- The active snapshot shows that same virtual display primary at `2560x1600`, the physical panel still
  extended, and a separately planned H.264 SDR stream at `1280x720@60`.
- The APK rendered 12 moving frames with 11 pixel variants. Authenticated input reached only the
  session-owned SessionProbe window, which recorded F12.
- The transaction retained the session across active disconnect, reconnected with a fresh ticket,
  handled explicit quit, observed the owned process exit, and removed the exact display lease.
- The runner emitted `BEACON_GATE5_PRODUCTION_SESSION_OK`. The external guard and outer `finally`
  cleanup both completed, ending with physical `DISPLAY1` primary, mirror mode disabled, no virtual
  output, zero HostAgent leases, and no active heartbeat.
- The R1 closeout matrix passes solution restore and format, warning-as-error build, all managed test
  projects, 28 Windows native tests, Android unit tests and debug assembly, and Client Lab lint plus 16
  tests. Android validation used the healthy installed Temurin 21 JDK because the local Zulu 21 image
  reports a modified `lib/modules` file.

## Active Outcome

R2 Playable Personal Build: complete one sustained game session from the Z Fold 7 through the existing
Beacon-owned path, with synchronized audio, production controller input, physical-client benchmarking,
a sustainable server-owned plan, reconnect, quit, and verified physical restore.

The controller slice is implemented end to end. Retained evidence in
`.artifacts/gate5-production-4988126f3e98478baab10bbe5fc6a477/` shows the normal APK path delivering
Xbox A through StreamCore, StreamWorker, and the Windows ViGEm sink to XInput, while also preserving
moving video, F12, disconnect/reconnect, explicit quit, controller release, and a restored snapshot
with `activeControllerSessions=0`. A late queued controller packet that initially recreated the target
after quit is now fenced by session lifecycle.

The display guard failure following that transaction was isolated from the product path. On this
machine, Windows can reject `QDC_ONLY_ACTIVE_PATHS` while exposing the active GDI display as an
available all-path CCD route. Beacon now reconstructs and validates that route with normalized mode
indexes. Signed HostAgent update workflow `30770997316` deployed source `2f1bd48`; the installed agent
reports `driverReady=true`, physical `DISPLAY5` primary at `2560x1600@240`, mirror disabled, and zero
leases. The final guarded controller rerun was not started because the Windows input desktop was
`Screen-saver`, so the existing fail-closed topology gate remained intact.

## Current Blocker

No R2 implementation blocker has been established. The next product slice is synchronized audio:
one Windows capture source, one Opus transport path, and one Android playback sink bound to the
existing Beacon session. The guarded controller transaction is retained for the final integrated
confirmation when the Windows input desktop is `Default`; it does not block audio implementation.

## Next Falsifiable Proof

The next proof must carry audible, ordered Opus audio from one Windows session capture source through
the authenticated Worker/StreamCore connection to Android playback, then stop and release capture,
decoder, and playback ownership on explicit quit. It must not change the established display, video,
controller, reconnect, quit, or recovery behavior.

## Prior Evidence

- `.artifacts/gate5-production-87104c13eaae4417953cd141bf2e3aef/` records the earlier transient
  `GraphicsCaptureItem::TryCreateFromDisplayId` failure (`0x80070490`) after primary activation. A
  standalone DisplayId probe and WGC probe subsequently succeeded against the same virtual output.
- `.artifacts/gate5-production-5c4d63e57e1e4069802495aa25559207/` records locked-session display
  preparation failing closed with HTTP 503 while cleanup retained physical-only topology.
