# Beacon Release Blocker

Updated: 2026-08-02

## Active Outcome

R1 Integrated Streaming Proof, Checkpoint 3: production media start.

## Last Verified Checkpoint

- Deployed code checkpoint `d8c3bf7a3e31afb91b340d4551c29ba332616f53` is recorded on the synchronized
  `codex/beacon-production-benchmarks` branch.
- Installed HostAgent source is `d8c3bf7a3e31afb91b340d4551c29ba332616f53`, supervised by the elevated
  `Beacon Stream Host Agent` scheduled task.
- HostAgent reports SudoVDA protocol `0.2.1`, zero retained leases, and healthy final-session
  closure before the run.
- Windows begins and ends with only physical `DISPLAY5` at `2560x1600@240`, primary, with no mirror
  mode.
- `emulator-5554` is online and Android reports boot complete.
- The retained prepared snapshot proves the fixture virtual display is extended at
  `2560x1600@120`, the physical panel remains primary, and the logical lease maps to
  `DISPLAY34` before launch.
- The production transaction passes prepared-display validation, emulator validation, TCP
  preflight, certified benchmark, session preflight, application launch, virtual-primary
  activation, transport authentication, and Android MediaCodec startup.
- The post-primary heartbeat gate is deployed. It requires consecutive observations of the
  intended virtual-primary/physical-extended topology before media start.
- Locked-session display preparation now fails cleanly and releases its lease instead of falling
  back to `DisplaySwitch.exe`, which was proven to detach the physical panel.
- Locked-session production evidence in
  `.artifacts/gate5-production-5c4d63e57e1e4069802495aa25559207` confirms `/beacon` returns `503`
  after the signed helper exits with code `5`; the final lease is released and Windows remains on
  physical-only `DISPLAY5` at `2560x1600@240`, primary, without mirror mode.

## Current Validation Constraint

The post-primary heartbeat gate still needs one unlocked production rerun. The active Windows input
desktop is currently `Screen-saver`; Windows rejects the signed helper's direct `SetDisplayConfig`
request in that state, and Beacon intentionally refuses the unsafe `DisplaySwitch.exe` fallback.
Production validation therefore requires the owning Windows session on the `Default` desktop.

The last unlocked production acceptance run failed when the authenticated APK asked StreamWorker to
start the first production video generation. `GraphicsCaptureItem::TryCreateFromDisplayId` rejected
the newly primary virtual output with Win32 `0x80070490` (`ERROR_NOT_FOUND`) at failure stage
`capture-item-display-id-create`, so StreamWorker closed the authenticated transport before media
evidence was produced.

Evidence:

- `.artifacts/gate5-production-87104c13eaae4417953cd141bf2e3aef/prepared-snapshot.json`
- `.artifacts/gate5-production-87104c13eaae4417953cd141bf2e3aef/failure-snapshot.json`
- `.artifacts/gate5-production-87104c13eaae4417953cd141bf2e3aef/server-output.log`
- `.artifacts/gate5-production-87104c13eaae4417953cd141bf2e3aef/session-probe.jsonl`
- `.artifacts/gate5-production-87104c13eaae4417953cd141bf2e3aef/display-id-capture-probe.log`
- `.artifacts/gate5-production-87104c13eaae4417953cd141bf2e3aef/wgc-capture-probe.log`

The retained snapshot and session probe show `DISPLAY34` active at `2560x1600@120`, primary,
extended with physical `DISPLAY5`, and hosting the launched probe window. A standalone DisplayId
probe and the full WGC capture probe both succeed against the same `DISPLAY34` after failure; the
WGC probe receives changing `2560x1600` frames through the NVIDIA capture device. Cleanup restores
physical-only topology and releases the lease successfully.

The first unattended post-update validation attempts failed earlier during display preparation
because the active Windows desktop was `Screen-saver`. `GetShellWindow()` is desktop-local, so the
elevated HostAgent could not resolve Explorer even though the owning user's Explorer process was
healthy in the same interactive session. The topology helper now falls back to the oldest Explorer
process in the current session, never selects an Explorer process from another session, and binds
the de-elevated helper explicitly to `winsta0\default` instead of inheriting the locked
`Screen-saver` desktop. Windows still rejects the helper's direct `SetDisplayConfig` request while
the session is locked. R1 therefore fails display preparation and cleans up in that state; it does
not fall back to `DisplaySwitch.exe`, which was proven to detach the physical panel while reporting
success. Emulator-backed production validation requires the owning Windows session on `Default`.

## Hypothesis Under Test

Before `d8c3bf7`, `WindowsDisplayApi.SetVirtualPrimaryAsync` returned immediately after requesting
the primary transition, while virtual-display creation already waited for consecutive
post-heartbeat topology evidence. The deployed gate tests whether that timing gap let the APK
authenticate and request media while Windows Graphics Capture still rejected the newly-primary
DisplayId, even though CCD and EnumDisplayDevices already reported the intended topology.

## Next Falsifiable Proof

Unlock the owning Windows session, confirm its input desktop is `Default`, rerun the same production
command, and inspect whether WGC opens without the transient DisplayId failure:

```powershell
.\scripts\test-gate5-production-session.ps1 -Serial emulator-5554 -ArtifactsReady
```

- Success advances R1 to encoded-frame and APK-render evidence.
- The same `0x80070490` failure after the gate proves topology convergence is insufficient and
  authorizes a target-specific StreamWorker capture-readiness handshake before ticket issuance.
