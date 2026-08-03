# Beacon Core Hardening Execution Design

Status: authoritative execution design, 2026-08-04

## Purpose

This design corrects Beacon's release sequence after the completed H.264 SDR transaction was treated
too broadly as proof of the streaming core. That transaction remains valuable integration evidence,
but it does not prove HDR, a production 10-bit codec path, or durable virtual-display recovery.

Beacon now freezes secondary expansion until two core outcomes pass:

1. one selected virtual-display driver passes capability, ownership, update, persistence, and
   recovery validation; and
2. one complete production HDR path passes from that virtual display to a physical Android display.

The active implementation plan is
`../plans/2026-08-04-beacon-core-hardening-outcome-gates.md`.

## Audited Production Baseline

Status uses the ordered evidence states defined by `REQ-TEST-017`; passing a lower state never implies
a higher one.

| Capability | Current state | Current evidence | Missing production evidence |
| --- | --- | --- | --- |
| H.264 SDR stream | `emulator-validated` | Production WGC, D3D11, NVENC, MsQuic, StreamCore, and MediaCodec completed the retained R1 transaction. | Physical-client sustained qualification remains outside this capability statement. |
| HDR policy and diagnostics | `static-tested` | Planner policies, benchmark fields, Windows Advanced Color queries, fallback reasons, and tests exist. | No production HDR stream has executed. |
| Virtual-display HDR | `absent` on the proven production path | Beacon queries real Advanced Color state. Historical target-laptop SudoVDA evidence reported 8-bit SDR and rejected the HDR state change. | A currently installed candidate must prove HDR support and active 10-bit state on the target laptop. |
| Worker HDR | `absent` | Production Worker advertises `hdr10=false`; backend rejects an HDR plan and emits SDR. | 10-bit capture, conversion, encoding, and HDR protocol metadata. |
| Android HDR presentation | `modeled` | Android inventories decoder and display HDR capability and carries HDR fields through contracts. | Production 10-bit decode and physical HDR presentation evidence. |
| HEVC/AV1 | `modeled` | Planner and benchmark contracts represent both codecs. | Production Worker encoder, transport, StreamCore decoder, and end-to-end runs. |
| Driver lease heartbeat and topology reconciliation | `target-laptop-validated` for the normal transaction | Per-client lease, heartbeat, topology quorum, guarded cleanup, and signed update mechanics have target-laptop evidence. | The complete abnormal-event recovery matrix is not closed. |
| Durable driver recovery | `absent` | Requirements and an acceptance-only display guard exist. | HostAgent lease journal, production supervisor, startup reconciliation, crash/power/PnP/GPU/hotplug matrix, and clean-machine proof. |

This baseline is deliberately blunt. Mock capabilities, successful API return values, and fallback
tests establish behavior contracts, not production feature support.

## Decision

### Core Stage 1: Driver Capability And Recovery

Beacon first settles the driver boundary because HDR and every later stream depend on it.

The first checkpoint is a reversible HDR feasibility and driver-selection proof:

- probe the current SudoVDA package through Windows Advanced Color 2 and actual 10-bit mode evidence;
- if it cannot satisfy the contract, evaluate an adapted Nonary `libvirtualdisplay` driver primitive,
  using Vibeshine as implementation evidence rather than a runtime dependency;
- require IddCx 1.10 HDR mode reporting, FP16 swapchain support, 10-bit mode and dithering evidence,
  HDR metadata handling, exact display identity, and a controllable per-client lease protocol; and
- stop with a boundary-specific decision report if neither candidate can prove viability.

After a candidate passes feasibility, Beacon hardens only that driver path:

- signed install, update, rollback, and exact PnP-instance verification;
- HostAgent-owned atomic lease and physical-baseline journal;
- startup adoption and reconciliation of exact Beacon-owned displays;
- event-driven recovery for Beacon Service, StreamWorker, HostAgent, and supervisor termination;
- power resume, unlock, sign-out, driver disappearance/restart, GPU reset, hybrid-adapter migration,
  dock/external-monitor changes, and display-name renumbering;
- preservation of owned applications while the inactive **AND** no-owned-work gate is false; and
- clean-machine install, recovery, rollback, uninstall, and topology preservation.

Stage 1 does not pass merely because normal create and remove work. It passes only with the retained
failure matrix and the laptop-integrity closeout.

### Core Stage 2: End-To-End HDR

Beacon then closes every HDR boundary through the one production architecture:

```text
HDR-capable virtual display
-> Windows HDR supported and active at 10+ bits
-> production capture in an HDR-preserving format
-> 10-bit conversion and hardware encoding
-> Beacon color and HDR metadata
-> StreamCore 10-bit decoder configuration
-> Android HDR Surface presentation
-> physical-device evidence
```

HDR requires a real 10-bit codec. HEVC Main10 is the default first candidate because it is broadly
available in the referenced Sunshine path; AV1 10-bit may be selected first if measured server and
client capability makes it the viable route. This adapter is part of HDR closure, not general codec
expansion. The remaining codec adapters stay deferred.

The acceptance session uses `HdrPreference.Require`. `prefer` falling back to SDR proves fallback,
not HDR. The evidence must include Windows Advanced Color state, source and conversion formats,
encoded profile and bit depth, colorimetry and HDR metadata, Android decoder output, Android display
HDR mode, moving frames, and the final laptop-integrity closeout.

If HDR cannot be made technically viable, Stage 2 ends as `decision-required` with the exact failing
boundary and retained SDR evidence. Beacon does not silently pivot to Vibeshine or declare fallback a
pass. At that point the product choices include adapting more upstream primitives, using Vibeshine as
the server implementation with Beacon benchmarking/orchestration, or explicitly removing HDR from
the product requirement.

### Later Outcomes

Only after Core Stages 1 and 2 pass may work resume in this order:

1. physical Z Fold 7 playable transaction using the already implemented audio, controller, and
   benchmark paths;
2. tablet keyboard/mouse and Android motion input;
3. remaining HEVC/AV1 codec coverage and per-client benchmark selection;
4. multi-client qualification, packaging, clean-machine product installation, and prerelease; and
5. UI refinement supported by observed usability defects.

Implemented secondary work is preserved, but it is not the active delivery target.

## Mandatory Laptop Integrity Closeout

Every stage or checkpoint that mutates the display driver or Windows topology must:

1. capture the internal panel's durable identity and physical-only baseline before mutation;
2. arm an independent recovery owner before mutation;
3. perform compensating restore in `finally`, even when the feature assertion fails;
4. wait on Windows topology generations and explicit driver/PnP acknowledgements rather than a
   cancellation timeout;
5. independently prove the internal panel is active and primary at its baseline resolution, refresh,
   orientation, and aspect ratio;
6. prove mirror mode is disabled, no inactive Beacon virtual output or lease remains, and the driver
   control session is closed; and
7. prove the interactive input desktop and normal Windows shell remain locally usable.

The evidence is retained with the stage result. Any closeout failure fails the stage and blocks the
next one, regardless of whether the feature assertion passed.

## Evidence And Claims

Each core-stage report states:

- capability state before and after the stage;
- exact source, driver, service, Worker, APK, and package revisions;
- static, emulator, target-laptop, and physical-client evidence separately;
- first unresolved boundary, when present;
- laptop-integrity closeout result; and
- the next falsifiable proof.

Beacon may claim a capability only at the highest level directly supported by retained production
evidence. The words `implemented`, `validated`, `supported`, and `complete` must include the evidence
level when ambiguity is possible.

## Technical Basis

Microsoft's IddCx 1.10 HDR contract requires the newer target-capability and mode DDIs, FP16 surface
handling, 10-bit mode information, and HDR metadata handling. Candidate code must satisfy that
contract rather than only adding HDR metadata to an EDID or accepting an Advanced Color request.

Primary references:

- [Microsoft IddCx 1.10 HDR updates](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/iddcx1.10-updates)
- [Microsoft IDDCX adapter capability flags](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/iddcx/ne-iddcx-iddcx_adapter_flags)
- [Nonary libvirtualdisplay](https://github.com/Nonary/libvirtualdisplay)
- [Vibeshine virtual display integration](https://github.com/Nonary/vibeshine/blob/vibe/src/platform/windows/virtual_display_sunshine.cpp)
- [Sunshine HDR requirements](https://docs.lizardbyte.dev/projects/sunshine/latest/md_docs_2getting__started.html)

## Stop Conditions

Execution stops and reports instead of expanding scope when:

- no driver candidate can make Windows report a real HDR-capable 10-bit virtual output;
- the target GPU cannot provide a supported low-latency 10-bit encoder path;
- StreamCore or the physical client cannot present the selected 10-bit stream in HDR;
- a recovery scenario cannot preserve owned work or restore physical control deterministically; or
- the mandatory laptop-integrity closeout fails.

These are decision points, not invitations to hide the missing boundary behind settings or fallback.
