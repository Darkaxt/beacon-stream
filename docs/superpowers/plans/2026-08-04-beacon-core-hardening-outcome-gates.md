# Beacon Core Hardening Outcome Gates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove and harden one HDR-capable virtual-display driver, then validate one complete Beacon HDR stream before resuming secondary feature work.

**Architecture:** Beacon retains one server-owned control plane, one HostAgent privilege boundary, one StreamWorker, and one Android StreamCore path. Core Stage 1 selects and hardens the driver underneath the existing display contracts; Core Stage 2 extends the existing production media path to one 10-bit HDR codec without introducing an upstream runtime or alternate client route.

**Tech Stack:** .NET 9, C# 13, C++20, Windows IddCx/DisplayConfig/WGC/D3D11, NVENC initially, MsQuic, protobuf, Android NDK/JNI, MediaCodec, Java 21, PowerShell, WPF, Playwright, GitHub Actions.

---

## Plan Authority

This plan supersedes `2026-08-01-beacon-release-outcome-gates.md`. The completed R1 evidence is
retained, but R2 and R3 are paused. No new tablet input, motion, remaining codec, multi-client,
packaging, or UI work begins until Core Stages 1 and 2 pass.

Work is measured by stage outcomes, not commit count or passing isolated unit tests. At most one
checkpoint is active. Each checkpoint fixes the first production-path failure and stops when its
exit evidence is retained.

## Requirement Traceability

| Specification requirements | Plan coverage |
| --- | --- |
| `REQ-DISP-001` through `REQ-DISP-020` | Existing lease behavior is preserved and revalidated in Checkpoints 1.2 and 1.4. |
| `REQ-DISP-021` through `REQ-DISP-031` | Durable HostAgent ownership is implemented in Checkpoint 1.3 and exercised in Checkpoint 1.4. |
| `REQ-DISP-032` | The robustness claim remains prohibited until the target-laptop and clean-machine Stage 1 matrix passes. |
| `REQ-DISP-033` through `REQ-DISP-036` | Universal Safety Gate plus Checkpoints 1.1 through 1.4. |
| `REQ-HDR-001` through `REQ-HDR-008` | Checkpoints 2.1 through 2.3 prove selection, fallback, complete-chain behavior, and diagnostics. |
| `REQ-HDR-009` through `REQ-HDR-013` | Independent bitstream/postcondition evidence and the explicit `decision-required` branch in Stage 2. |
| `REQ-STREAM-007` and `REQ-STREAM-008` | Stage 2 adds the first required 10-bit adapter; remaining codec parity resumes only after core closure. |
| `REQ-TEST-011` through `REQ-TEST-018` | Stage 1 failure matrix, Stage 2 physical proof, evidence levels, clean-machine validation, and every-stage laptop closeout. |

This traceability table is a scope check, not acceptance evidence. A requirement closes only through
the corresponding retained production proof.

## Universal Safety Gate

Every checkpoint that can change the driver or topology must use the production-equivalent display
guard before mutation and run the mandatory closeout in `finally`.

The closeout must retain:

- the pre-mutation internal-panel identity and physical-only baseline;
- the final all-path and active-path DisplayConfig inventories;
- the internal panel active and primary at baseline resolution, refresh, orientation, and aspect;
- `mirrorMode=False`;
- zero inactive Beacon leases and no inactive-session Beacon virtual output;
- closed driver heartbeat/control state; and
- proof that the interactive input desktop and Windows shell are locally usable.

A closeout failure fails the checkpoint and stage, runs compensating recovery, and blocks the next
checkpoint. Feature success cannot override it. Waiting follows topology generations, PnP events,
driver acknowledgements, process exits, and explicit cancellation; no timeout owns lifecycle.

## Core Stage 1: Driver Capability And Recovery

### Checkpoint 1.1: Establish Driver And HDR Feasibility

**Files:**

- Create: `scripts/test-virtual-display-driver-capability.ps1`
- Create: `docs/validation/core-stage-1-driver-candidate.md`
- Modify: `src/Beacon.DisplayProbe/DisplayProbeCommandLine.cs`
- Modify: `src/Beacon.DisplayProbe/DisplayProbeApp.cs`
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayApi.cs`
- Test: `tests/Beacon.DisplayProbe.Tests/DisplayProbeAppTests.cs`
- Test: `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayDiagnosticsTests.cs`

- [ ] Extend the read-only probe result to record exact PnP instance, INF/provider/version, IddCx
  runtime version, adapter/target identity, supported and active Advanced Color flags, active color
  mode, bits per channel, pixel encoding, and selected display mode.
- [ ] Add an explicit guarded capability transaction that creates one per-client display, requests
  an aspect-correct target mode with HDR, observes Windows' real postcondition, and restores the
  physical baseline through the universal safety gate.
- [ ] Run the transaction against the currently packaged SudoVDA revision. API success, EDID HDR
  metadata, or a visible Windows toggle is insufficient; Windows must report HDR supported and
  active with at least 10 bits per channel.
- [ ] If SudoVDA fails, fork `Nonary/libvirtualdisplay` to `Darkaxt/libvirtualdisplay`, pin the exact
  upstream revision in the candidate report, build its signed test package, and run the same probe
  without changing Beacon's product control plane.
- [ ] Record a comparison covering IddCx 1.10 DDIs, FP16 swapchains, 10-bit mode/dithering support,
  HDR metadata, per-client identity, lease protocol, departure generation fencing, install/update,
  recovery hooks, licensing, and retained upstream tests.
- [ ] Select exactly one driver only when it passes the capability transaction and laptop-integrity
  closeout. If neither candidate passes, mark Stage 1 `decision-required`, publish the exact failing
  boundary, and stop all implementation work.

**Exit:** One pinned driver candidate produces a real HDR-capable 10-bit Windows virtual output and
the laptop returns to the verified physical baseline. The result is dynamic target-laptop evidence,
not a source inspection conclusion.

### Checkpoint 1.2: Integrate The Selected Driver Boundary

**Files:**

- Modify: `src/Beacon.Platform.Windows/Displays/IWindowsDisplayLeaseSession.cs`
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsSudoVdaDriverConnection.cs`
- Modify: `src/Beacon.Platform.Windows/Displays/SudoVdaDriverLeaseSession.cs`
- Modify: `src/Beacon.HostAgent/DriverUpdates/SudoVdaPackageManifest.cs`
- Modify: `src/Beacon.HostAgent/DriverUpdates/SudoVdaPackageValidator.cs`
- Modify: `src/Beacon.HostAgent/DriverUpdates/WindowsSudoVdaDriverPlatform.cs`
- Modify: `scripts/new-sudovda-package.ps1`
- Test: `tests/Beacon.Platform.Windows.Tests/Displays/SudoVdaDriverLeaseSessionTests.cs`
- Test: `tests/Beacon.HostAgent.Tests/SudoVdaPackageValidatorTests.cs`
- Test: `tests/Beacon.HostAgent.Tests/WindowsSudoVdaDriverPlatformTests.cs`

- [ ] Preserve the public Beacon display and HostAgent contracts while adapting the selected
  driver's control protocol behind them. Rename SudoVDA-specific internal types only where the
  selected protocol makes the old name false.
- [ ] Bind create, acquire, heartbeat, inspect, release, and remove to a stable per-client identity
  and driver-record generation. Never infer ownership from `DISPLAYx`, friendly name, or adjacency.
- [ ] Extend package validation and signed update/rollback evidence to the selected driver identity,
  control-interface ACL, protocol version, files, certificate, and exact expected PnP instance.
- [ ] Prove normal prepare, activate, active disconnect, reconnect, quit, removal, update, rollback,
  and HostAgent restart through the existing typed boundaries with no manual UAC interaction.
- [ ] Run the universal safety closeout and retain the installed binary/package hashes.

**Exit:** Beacon exclusively controls the selected driver through HostAgent, and the normal session
transaction plus update rollback ends at the verified physical baseline.

### Checkpoint 1.3: Add Durable Lease Recovery Ownership

**Files:**

- Create: `src/Beacon.HostAgent/Displays/DisplayRecoveryJournal.cs`
- Create: `src/Beacon.HostAgent/Displays/DisplayRecoveryStateStore.cs`
- Create: `src/Beacon.HostAgent/Displays/DisplayRecoverySupervisor.cs`
- Create: `src/Beacon.HostAgent/Displays/WindowsDisplayRecoveryEvents.cs`
- Modify: `src/Beacon.HostAgent/Program.cs`
- Modify: `src/Beacon.HostAgent/HostAgentDispatcher.cs`
- Modify: `src/Beacon.HostAgent/WindowsHostAgentDisplayExecutor.cs`
- Modify: `src/Beacon.HostAgent.Contracts/HostAgentDisplayPayloads.cs`
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayBackend.cs`
- Test: `tests/Beacon.HostAgent.Tests/DisplayRecoveryJournalTests.cs`
- Test: `tests/Beacon.HostAgent.Tests/DisplayRecoverySupervisorTests.cs`
- Test: `tests/Beacon.HostAgent.Tests/WindowsDisplayRecoveryEventsTests.cs`

- [ ] Implement one ACL-protected atomic HostAgent journal containing the physical baseline, exact
  driver/display identity, client, intended mode/topology, ownership state, lifecycle phase,
  generation, and last verified compensation.
- [ ] Reconcile driver inventory and Windows all-path topology against the journal before HostAgent
  accepts a display command and before Beacon Service accepts a launch.
- [ ] Implement a production supervisor for power resume, unlock, sign-out, shutdown, Service loss,
  HostAgent restart, driver/PnP state change, and explicit emergency recovery.
- [ ] Make recovery ordering evidence-driven: restore-before-remove or remove-before-restore is
  selected from the observed topology and physical baseline, then independently verified.
- [ ] Preserve the application and lease while inactive-client **AND** no-owned-work is false. When
  ownership cannot be recomputed after a crash, restore physical control but retain the journaled
  application and lease until recomputation is possible.
- [ ] Expose trigger, journal revision, baseline source, exact identities, topology generations,
  compensation actions, and final verification through diagnostics.

**Exit:** Abnormal component termination has a production owner and durable compensation evidence;
the acceptance-only guard is no longer the sole recovery owner.

### Checkpoint 1.4: Close The Recovery Matrix

**Files:**

- Create: `scripts/test-core-stage-1-driver-recovery.ps1`
- Create: `tests/Beacon.ProductionAcceptance/DriverRecoveryMatrix.cs`
- Modify: `tests/Beacon.ProductionAcceptance/ProductionDisplayGuard.cs`
- Modify: `scripts/test-gate5-production-session.ps1`
- Update: `docs/validation/core-stage-1-driver-candidate.md`

- [ ] Inject Beacon Service, StreamWorker, HostAgent, and recovery-supervisor termination at every
  prepare, activate, stream, disconnect, quit, restore, and remove phase.
- [ ] Exercise lock/unlock, sleep/resume, hibernate/resume, sign-out/sign-in, driver disable/re-enable,
  protocol mismatch, driver restart, GPU reset, hybrid-adapter remap, dock/external-monitor hotplug,
  and display-name renumbering.
- [ ] Verify owned applications survive repair, unrelated displays are untouched, and only
  inactive-client **AND** no-owned-work permits automatic lease removal.
- [ ] Run signed install, update, rollback, recovery, uninstall, and pre-install topology restoration
  on one clean Windows installation.
- [ ] Retain every failure injection, journal transition, topology generation, driver event, and
  mandatory laptop-integrity closeout.

**Stage 1 exit:** All selected-driver capability and recovery evidence is green on the target laptop
and clean machine. `docs/validation/beacon-release-blocker.md` records Core Stage 1 complete and links
the retained artifacts. Stop before Core Stage 2 implementation.

## Core Stage 2: End-To-End HDR

### Checkpoint 2.1: Implement One Production 10-Bit Worker Path

**Files:**

- Modify: `contracts/worker_ipc.proto`
- Modify: `contracts/stream_control.proto`
- Modify: `src/Beacon.StreamWorker/src/worker_host.cpp`
- Modify: `src/Beacon.StreamWorker/src/capture/wgc_display_capture.cpp`
- Modify: `src/Beacon.StreamWorker/src/capture/windows_wgc_capture_platform.cpp`
- Modify: `src/Beacon.StreamWorker/src/video/d3d11_video_processor.cpp`
- Modify: `src/Beacon.StreamWorker/src/video/nvenc_h264_encoder.cpp`
- Create: `src/Beacon.StreamWorker/src/video/nvenc_hevc_main10_encoder.cpp`
- Create: `src/Beacon.StreamWorker/include/beacon/worker/video/nvenc_hevc_main10_encoder.h`
- Modify: `src/Beacon.StreamWorker/CMakeLists.txt`
- Modify: `src/Beacon.Platform.Windows/Streaming/StreamWorkerStreamingBackend.cs`
- Create: `tests/Beacon.StreamWorker.Tests/nvenc_hevc_main10_encoder_tests.cpp`
- Modify: `tests/Beacon.StreamWorker.Tests/worker_video_pipeline_tests.cpp`
- Modify: `tests/Beacon.StreamWorker.Tests/CMakeLists.txt`
- Test: `tests/Beacon.Platform.Windows.Tests/Streaming/StreamWorkerStreamingBackendTests.cs`

- [x] Add explicit capture format, conversion format, codec profile, bit depth, color primaries,
  transfer function, matrix, range, mastering metadata, and content-light metadata to Worker
  capabilities, prepare requests, and session diagnostics.
- [x] Preserve HDR capture through FP16 or another proven HDR-preserving WGC/D3D11 format and convert
  to the encoder's 10-bit input without an 8-bit intermediate.
- [x] Implement HEVC Main10 behind the existing encoder interface. Select AV1 10-bit instead only if
  Checkpoint 1.1's measured server/client evidence proves it is the viable first adapter.
- [x] Keep `hdr10=false` until runtime probing proves the complete selected Worker path. Reject a
  mismatched HDR plan before display or application side effects.
- [x] Prove the encoded stream profile, bit depth, colorimetry, and HDR metadata with a parser that is
  independent of the encoder's own success return.

**Exit:** StreamWorker emits a verified moving 10-bit HDR bitstream from the selected virtual display
through the same Beacon media contract, while H.264 SDR tests remain green.

### Checkpoint 2.2: Carry And Present HDR In StreamCore

**Files:**

- Modify: `src/Beacon.Android/app/src/main/cpp/streamcore`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconStreamSession.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidDeviceCapabilityProbe.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidSystemBenchmarkHardwareSource.java`
- Test: `src/Beacon.Android/app/src/main/cpp/streamcore/tests`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android`

- [x] Map the selected 10-bit codec profile and Beacon HDR metadata to MediaCodec without a Java or
  alternate decoder route.
- [x] Configure the production Surface/window color mode for HDR and record decoder output format,
  color standard, transfer, range, bit depth, dropped frames, and Android display HDR state.
- [x] Make Android advertise HDR10 only after decoder inventory, 10-bit vector presentation, display
  HDR support, and active presentation all succeed.
- [x] Extend the hardware benchmark to qualify SDR and HDR candidates separately; emulator evidence
  validates contracts only and cannot certify physical HDR.
- [x] Prove resource release and SDR/HDR switching through the existing generation-owned StreamCore
  lifecycle with no leaked codec or Surface.

**Exit:** The standard APK can decode and present the production 10-bit stream through StreamCore,
and diagnostics distinguish decoded HDR from physically presented HDR.

**2026-08-04 status:** Checkpoint 2.1 is complete. Checkpoint 2.2 implementation and its static,
native, and emulator contract validation are complete. The emulator has no exact HEVC Main10 HDR10
decoder and cannot certify a physical HDR presentation, so the Checkpoint 2.2 exit and Checkpoint 2.3
remain open until a real HDR Android device runs the guarded transaction. See
`docs/validation/2026-08-04-hdr10-pipeline.md`.

### Checkpoint 2.3: Prove HDR And Fallback On Physical Hardware

**Files:**

- Create: `scripts/test-core-stage-2-hdr-session.ps1`
- Create: `tests/Beacon.ProductionAcceptance/HdrSessionAssertions.cs`
- Modify: `scripts/test-gate5-production-session.ps1`
- Modify: `docs/validation/beacon-release-blocker.md`

- [ ] Run the guarded physical-client transaction with `HdrPreference.Require` and the selected
  10-bit codec. Require real Windows Advanced Color support/active state and Android HDR
  presentation; prohibit SDR fallback from satisfying the assertion.
- [ ] Retain moving-frame evidence, encoded stream inspection, Windows HDR state, Android decoder
  output, Android display mode, benchmark revision, session plan, and end-to-end latency/drop data.
- [ ] Run `off` and `prefer` sessions to prove SDR stability and truthful fallback on an unsupported
  client without changing the virtual-display identity or corrupting topology.
- [ ] Disconnect, reconnect with a fresh ticket, quit, and run the universal safety closeout after
  every physical transaction.
- [ ] If any boundary remains impossible, write `docs/validation/core-stage-2-hdr-decision.md` with
  the exact blocker and viable product alternatives; mark the stage `decision-required` and stop.

**Stage 2 exit:** One retained physical-client transaction proves the complete `require` HDR chain,
fallback sessions remain stable, and every run ends with the internal laptop panel verified at its
physical baseline. Commit and synchronize the evidence before unfreezing later outcomes.

## Later Outcome Order

After both core stages pass:

1. complete the sustained Z Fold 7 playable transaction using existing audio/controller/benchmark;
2. implement tablet keyboard/mouse and DSU/Cemuhook Android motion;
3. add the remaining HEVC/AV1 adapters and measured per-client codec selection;
4. qualify multi-client behavior;
5. package, clean-machine validate, and publish the prerelease; and
6. refactor only from retained production evidence, then repeat static/dynamic validation and sync.

Every later outcome inherits the universal safety gate. No later stage may leave the laptop panel
inactive, non-primary, mirrored, on the wrong aspect ratio, or dependent on a rescue helper.

## Status Reporting Template

```text
Active core stage: 1 / 2 / later outcome
Capability level: absent / modeled / static-tested / emulator-validated /
                  target-laptop-validated / physical-client-validated
Last completed checkpoint: <name and retained evidence>
Current production blocker: <single first failing boundary>
Laptop integrity: <baseline identity and final verified result>
Next falsifiable proof: <one observable transaction>
Decision required: no / <exact unresolved product choice>
```
