# Beacon 80/20 Release Execution Design

Status: authoritative execution-policy revision, 2026-08-01

## Purpose

Beacon's product architecture remains the architecture defined by
`2026-06-03-personal-streaming-orchestrator-design.md`. This document changes how that
architecture is delivered. It replaces component-count progress and exhaustive first-pass
completion with three observable release outcomes.

The immediate objective is not to make every subsystem complete. It is to prove the smallest
Beacon-owned transaction that behaves like the intended product, then add only the capabilities
needed to make that transaction playable and distributable.

## Problem

The previous execution plan decomposed the system into many individually valid slices. Most of
those slices now pass, but they do not constitute a working product until the production path can
complete one transaction:

```text
prepare display
-> launch catalog application
-> stream moving video
-> deliver input
-> disconnect and reconnect
-> stop
-> restore the physical desktop
```

Counting commits, tests, or checked boxes overstated progress while this transaction remained
unproved. It also encouraged speculative hardening and cleanup at every local boundary. That is
the wrong progress model for the remaining work.

## Decision

Work is now governed by three release outcome gates:

1. **R1 Integrated Streaming Proof** proves one minimal complete transaction in the Android
   emulator.
2. **R2 Playable Personal Build** adds only what is required to play from the intended physical
   phone.
3. **R3 Functional Prerelease** packages and validates the working system for repeatable personal
   installation.

The active implementation plan is
`../plans/2026-08-01-beacon-release-outcome-gates.md`. Older milestone plans remain historical
evidence, not parallel backlogs.

## R1: Integrated Streaming Proof

R1 uses the existing production acceptance runner as the sole product-progress meter:
`scripts/test-gate5-production-session.ps1` backed by
`tests/Beacon.ProductionAcceptance/Program.cs`.

R1 passes when that runner proves all of the following in one transaction:

- a stable `2560x1600` per-client virtual display is prepared without mirror mode or physical
  capture fallback;
- a deterministic application selected from the real server catalog is launched on that display;
- the standard APK on `emulator-5554` renders moving H.264 SDR video from that display;
- one authenticated keyboard/input action reaches the launched application;
- an active disconnect preserves application and display ownership;
- reconnect uses a fresh ticket and the same owned session;
- explicit quit closes the deterministic application;
- the inactive **AND** no-owned-work rule removes the lease and restores verified physical-primary
  topology; and
- the runner emits `BEACON_GATE5_PRODUCTION_SESSION_OK` with retained evidence.

R1 intentionally does not require:

- audio;
- controller support beyond the existing minimal input proof;
- HDR, HEVC, AV1, FEC, or multiple codec selection;
- a physical-phone run;
- 120 FPS or sustained-performance qualification;
- broad fault injection, stress matrices, or fast repetition;
- UI refinement;
- a generalized updater or final installer; or
- a broad cleanup/refactor pass.

A defect discovered on the R1 path is part of R1. A capability outside that path is deferred even
when it appears easy to add.

## R2: Playable Personal Build

R2 starts only after R1 evidence is committed. It adds the minimum capabilities required for the
owner to play a game from the Z Fold 7:

- synchronized game audio through one production audio path;
- one production controller path;
- automatic and manual network-and-hardware benchmark execution on the physical client;
- a benchmark-selected sustainable session plan;
- catalog selection, launch, stream, input, reconnect, quit, and physical restore on the phone;
  and
- one sustained personal gameplay session with no unresolved display ownership.

R2 does not require HDR, HEVC, AV1, multi-client concurrency, a settings-heavy client, live policy
editing, internet relay, or broad UX polish. H.264 SDR remains an acceptable release path. The APK
remains a thin client with catalog selection, benchmark controls, stream presentation, input, and
client-local interaction settings.

R2 is the first build that may be described as playable. R1 is an integration proof, not a user
release.

## R3: Functional Prerelease

R3 turns the playable build into a repeatably installable personal release:

- one Windows installation path installs and starts all required Beacon-owned components;
- the Android APK is published as a matching artifact;
- startup, update, rollback, uninstall, and privileged driver/helper operations have explicit
  ownership and diagnostic evidence;
- a clean-machine installation completes the R2 user journey;
- the full relevant static validation matrix and CI pass;
- one evidence-driven refactor removes only duplication or ownership ambiguity observed in the
  working path; and
- the complete acceptance transaction passes before and after that refactor.

R3 produces a GitHub prerelease. It does not imply feature parity with Sunshine, Apollo,
Moonlight, or their forks.

## Execution Rules

### One Active Blocker

Only one release blocker may be active. Work starts at the first failed observable checkpoint in
the current release outcome. No parallel feature work begins while that blocker is unresolved.

The blocker record contains only:

- current release and checkpoint;
- last observed evidence;
- the narrow failing boundary;
- one current hypothesis; and
- the next falsifiable verification.

The record is updated when evidence changes, not for routine activity.

### Advance The Transaction

Every production change must advance the next observable checkpoint of the active transaction.
Focused component tests prevent regressions but do not count as product milestones. A commit,
pull request, passing unit suite, or implemented abstraction is not release progress unless it
changes the transaction's last verified checkpoint.

### Fix The First Failure

The acceptance runner is executed until it reaches the first failing boundary. Investigation and
changes remain scoped to that boundary. Later failures are not guessed at or preemptively fixed.

Event-driven state transitions, driver heartbeats, process exits, protocol messages, and verified
topology generations are the synchronization mechanisms. Cancellation timeouts and arbitrary
sleeps are prohibited.

### Refactor From Evidence

Before the first R1 pass, refactoring is allowed only when direct evidence shows that an ownership
or synchronization defect cannot be corrected locally. Broad cleanup, abstraction improvement,
and theoretical hardening wait until the product transaction works.

R3 contains one deliberate refactor pass after a working acceptance baseline exists. The same
acceptance evidence must pass afterward.

### Validate At Outcome Boundaries

While a blocker is active, run the narrowest tests that prove the changed boundary plus the next
production checkpoint. Run the complete static and dynamic matrix once when an outcome gate is
otherwise ready to close. Do not repeatedly run the full matrix between local edits.

### Report Outcomes

Status reports use user journeys and observable checkpoints:

- last complete outcome;
- last verified transaction checkpoint;
- current blocker;
- next proof; and
- deferred release capabilities.

Commit totals and checklist percentages may be supporting context, never the headline measure.

## Authority And Scope

This document controls execution priority and release claims. The architecture, security,
server-owned policy, client role, virtual-display lifecycle, and inactive **AND** no-owned-work
invariants in the main design remain binding.

The following are superseded as active task authorities:

- unchecked work in `2026-07-10-beacon-stream-gates-3-5.md`;
- any README statement that classifies already implemented production video primitives as future
  Gate 5 work; and
- any component-level checklist that competes with the current release transaction.

Those documents and checks remain useful as implementation history and regression evidence.

## Stop Conditions

After each release outcome passes, stop feature implementation long enough to commit its evidence,
update truthful product status, and choose the next outcome deliberately. Do not silently expand an
outcome with attractive deferred work.

If R1 cannot advance, the plan is changed only from evidence showing that its current architecture
cannot satisfy the minimal transaction. Difficulty, test count, or elapsed effort alone does not
justify adding another layer.
