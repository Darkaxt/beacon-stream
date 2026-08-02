# Beacon Release Blocker

Updated: 2026-08-02

## Active Outcome

R1 Integrated Streaming Proof, Checkpoint 3: production media start.

## Last Verified Checkpoint

- Checkout `dd7be1984d08f40dfba951ab16ed40aed2786d57` is synchronized with
  `origin/codex/beacon-production-benchmarks`.
- Installed HostAgent source is `0f6e3d02271c98b1ccdd200a2f46d571669fef12`, supervised by the elevated
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

## Current Blocker

The production acceptance runner fails when the authenticated APK asks StreamWorker to start the
first production video generation. `GraphicsCaptureItem::TryCreateFromDisplayId` rejects the newly
primary virtual output with Win32 `0x80070490` (`ERROR_NOT_FOUND`) at failure stage
`capture-item-display-id-create`, so StreamWorker closes the authenticated transport before media
evidence is produced.

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
process in the current session; it never selects an Explorer process from another session.

## Current Hypothesis

`WindowsDisplayApi.SetVirtualPrimaryAsync` returns immediately after requesting the primary
transition, while virtual-display creation waits for consecutive post-heartbeat topology evidence.
The APK can therefore authenticate and request media while Windows Graphics Capture still rejects
the newly-primary DisplayId even though CCD and EnumDisplayDevices already report the intended
topology.

## Next Falsifiable Proof

Deploy the post-primary heartbeat gate, rerun the same production command, and inspect whether WGC
opens without the transient DisplayId failure:

```powershell
.\scripts\test-gate5-production-session.ps1 -Serial emulator-5554 -ArtifactsReady
```

- Success advances R1 to encoded-frame and APK-render evidence.
- The same `0x80070490` failure after the gate proves topology convergence is insufficient and
  authorizes a target-specific StreamWorker capture-readiness handshake before ticket issuance.
