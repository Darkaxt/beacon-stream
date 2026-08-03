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

Core Stage 1 Driver Capability And Recovery: select one virtual-display driver only after it proves a
real HDR-capable 10-bit Windows output, then close its durable ownership, update, reconciliation, and
abnormal-event recovery matrix. Core Stage 2 end-to-end HDR blocks all later product work.

The completed R1 transaction is H.264 SDR integration evidence. It does not establish HDR, production
HEVC/AV1, or durable driver recovery. The production Worker currently advertises `hdr10=false`,
rejects HDR plans, and emits H.264 SDR. HDR planner and diagnostics tests establish truthful fallback
behavior only.

Every driver/topology checkpoint must arm an independent recovery owner and end with the laptop's
internal panel independently verified active and primary at its captured physical baseline, mirror
mode disabled, no inactive-session Beacon virtual output or lease, closed heartbeat/control state,
and a usable interactive desktop.

## Paused Secondary Evidence

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

The first blocker is driver/HDR feasibility, not the paused physical-client R2 transaction. Historical
target-laptop SudoVDA probe evidence reported Windows Advanced Color unsupported, 8 bits per channel,
and `ERROR_NOT_SUPPORTED` when changing HDR state. That evidence is not current enough to reject HDR,
but it is sufficient to prohibit assuming the existing driver works.

The active plan must run one guarded, current capability transaction against the packaged SudoVDA
revision. If it fails, the same transaction evaluates a pinned fork of Nonary `libvirtualdisplay`,
whose current source implements the IddCx 1.10 HDR DDIs, FP16 capability, 10-bit mode/dithering data,
HDR metadata handling, and driver-record generation fencing. Source support is candidate evidence;
only the Windows postcondition on this laptop selects the driver.

After selection, Core Stage 1 still lacks a HostAgent-owned display recovery journal, startup
reconciliation, production recovery supervisor, abnormal-event matrix, and clean-machine proof. Core
Stage 2 then lacks the entire production HDR stream: HDR-preserving capture, 10-bit conversion,
HEVC Main10 or AV1 10-bit encoding, Beacon HDR metadata, StreamCore decoding, and physical Android HDR
presentation. These boundaries cannot be reported as implemented from planner or fallback tests.

## Next Falsifiable Proof

Implement `scripts/test-virtual-display-driver-capability.ps1` and run it first against the exact
packaged SudoVDA binary, with the independent display guard armed before monitor creation. The proof
must record driver identity, IddCx runtime, exact per-client output, Advanced Color 2 flags, active
color mode, bits per channel, pixel encoding, and topology generations. It must then remove the test
lease and independently verify the internal panel active and primary at its captured physical mode,
mirror disabled, zero Beacon leases, no heartbeat, and a usable input desktop.

If SudoVDA cannot produce an HDR-supported, HDR-active 10-bit output, repeat the exact transaction with
a pinned `Darkaxt/libvirtualdisplay` fork. If neither candidate succeeds, stop and publish the exact
driver/Windows boundary for a product decision; do not proceed to controller, tablet, motion, remaining
codec, packaging, or UI work.

## Prior Evidence

- `.artifacts/gate5-production-87104c13eaae4417953cd141bf2e3aef/` records the earlier transient
  `GraphicsCaptureItem::TryCreateFromDisplayId` failure (`0x80070490`) after primary activation. A
  standalone DisplayId probe and WGC probe subsequently succeeded against the same virtual output.
- `.artifacts/gate5-production-5c4d63e57e1e4069802495aa25559207/` records locked-session display
  preparation failing closed with HTTP 503 while cleanup retained physical-only topology.
