# Beacon Release Blocker

Updated: 2026-08-04

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

The R2 audio path is now implemented from Windows WASAPI loopback capture and Opus encode through
the authenticated Worker transport, native Android Opus decode, JNI PCM delivery, and one
generation-owned `AudioTrack`. Retained evidence in
`.artifacts/gate5-production-3fc9419b28e74b8a92b58e78a5a89313/` records the owned SessionProbe
tone on the virtual primary, 72 PCM frames written during the initial connection, and 63 after a
fresh-ticket reconnect. The product runner emitted `BEACON_GATE5_PRODUCTION_SESSION_OK`; its outer
script then failed only because a redundant DisplayConfig restore rejected missing source-mode
metadata after `/internal` had already restored a verified physical-only topology.

The redundant restore is now idempotent. A following run proved both outer restore calls, zero
leases, and physical-only verification, but also exposed a delayed hybrid-panel collapse after two
apparently stable topology heartbeats. Commit `6feac4b` raises the production topology quorum to four
matching heartbeat observations and resets it on any path, fingerprint, or desired-state change.
Signed Host Agent workflow `30776554104` deployed that exact revision through the supervised
bootstrap.

The Beacon-native fake-display emulator transaction is now green. Retained evidence in
`.artifacts/gate3-r2-benchmark-8e0edc965d6243e79c37b64a65985572/` covers the certified benchmark
and session preflight, native hardware observation, initial stream, fresh-ticket reconnect, active
Worker crash isolation, and emergency recovery. Network evidence now records zero throughput for
missing packets instead of assigning them the aggregate measured throughput. The hardware benchmark
is terminal and retained whether the emulator run accepts it or capability-rejects it; no benchmark
remains pending. An independent post-run check found the physical `DISPLAY5` primary at
`2560x1600@240`, mirror mode disabled, no active virtual display, zero HostAgent leases, and no active
heartbeat. This proves the fake-display emulator transaction, not R2 completion; final physical Z Fold
7 confirmation remains required.

Client display planning now consumes structured current and supported mode facts from Android,
Client Lab, and FakeEndpoint. The server persists one complete selected mode per client, prefers an
exact configured target, preserves aspect ratio during fallback, and no longer assigns the universal
`2560x1600` default to newly registered clients. Version 1 scalar profile files migrate atomically to
the structured version 2 model so existing per-client setup is retained.

## Current Blocker

The R2 server-side and emulator preflight pipeline is complete. Commit `3639156` made TestHost-owned
Worker disposal deterministic without a timeout, and CI run `30853338297` passed the full repository
matrix. The Android job crossed the previously stalled boundary in order: application shutdown,
`BEACON_HOSTED_WORKER_STOPPED`, then `BEACON_HOSTED_EMULATOR_BENCHMARKS_OK`.

The remaining R2 acceptance blocker is the guarded physical Z Fold 7 transaction. The runner now has
an isolated physical-client mode, exact physical/emulator client identities, restore-first cleanup,
and current-invocation instrumentation evidence, but emulator success does not certify the phone's
radio, decoder, thermal, audio, controller, or human-experience behavior.

Version-one input has one additional implementation gap that is not part of the controller-focused R2
exit: the authenticated transport and Windows sink accept pointer, keyboard, and controller events in
the same session, but the APK currently captures touch and physical controller events only. Tablet
hardware mouse/keyboard capture must use per-client capabilities, including captured relative mouse
movement for games, without introducing global input modes or affecting the phone controller path.

Android-device motion is also a registered version-one capability, but it follows the basic tablet
input slice rather than expanding R2. It does not require a motion-capable virtual gamepad or Windows
kernel driver. Beacon still needs timestamped gyroscope and accelerometer capture in the APK, transport
through the authenticated input channel, and a session-owned DSU/Cemuhook UDP server bound to Windows
loopback for Cemu-class emulators. The existing controller route remains independent. Until the DSU
exchange is proven, motion is reported as unsupported and is never silently remapped to mouse,
right-stick, or virtual-controller input.

Codec planning and benchmark contracts already model H.264, HEVC, and AV1, including per-client
capabilities and measured codec results. The full server-interpreted benchmark owns automatic codec
selection and persists a ranked qualified set; launch planning consumes that result, while the
lightweight session preflight may move only to another benchmark-qualified fallback. The production
Worker currently advertises, encodes, and transports H.264 only. That is sufficient for the bounded
R2 transaction and first packaged prerelease, but not for final version-one completion. Production
HEVC and AV1 must reuse the same Worker, Beacon transport, StreamCore, benchmark, and planner
boundaries. A server-owned client profile may explicitly constrain the benchmark candidates.

## Next Falsifiable Proof

Run the guarded production transaction in physical-client mode from the Z Fold 7 with the installed
four-heartbeat quorum. It must retain the exact physical client identity, sustainable benchmark plan,
moving video, ordered PCM writes on initial connect and reconnect, controller input, explicit quit, and
current-invocation Android evidence; exit the outer runner with zero; and independently verify one
physical primary display, zero leases, and no heartbeat. Physical Z Fold 7 audible confirmation remains
the final human audio check.

After that R2 proof, add the bounded tablet input slice: hardware keyboard down/up mapping, absolute
mouse navigation, captured relative movement, buttons, and wheel through the existing authenticated
input stream. Its acceptance must prove mouse and keyboard on the tablet profile while the separate
phone profile continues to expose controller input.

The following motion slice must detect the Android device gyroscope and accelerometer, normalize and
transport timestamped samples, publish them through a loopback DSU/Cemuhook server, cleanly unregister
listeners with the owning session, and pass a fake Cemu-style DSU version/list/subscribe/data exchange
before advertising motion support. Physical acceptance then verifies orientation, latency, and drift.

After the first packaged prerelease, add production HEVC and AV1 as codec adapters behind the existing
Worker and StreamCore contracts. Acceptance must benchmark all server/client-supported candidates,
prove independent H.264, HEVC, and AV1 sessions in emulator or Client Lab, and prove on physical
hardware that an AV1-capable client can receive an AV1 plan while unsupported or unsustainable clients
fall back with an explicit reason.

## Prior Evidence

- `.artifacts/gate5-production-87104c13eaae4417953cd141bf2e3aef/` records the earlier transient
  `GraphicsCaptureItem::TryCreateFromDisplayId` failure (`0x80070490`) after primary activation. A
  standalone DisplayId probe and WGC probe subsequently succeeded against the same virtual output.
- `.artifacts/gate5-production-5c4d63e57e1e4069802495aa25559207/` records locked-session display
  preparation failing closed with HTTP 503 while cleanup retained physical-only topology.
