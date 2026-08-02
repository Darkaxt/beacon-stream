# Beacon Release Blocker

Updated: 2026-08-02

## Active Outcome

R1 Integrated Streaming Proof: complete one guarded emulator-backed production transaction through
session-owned input, reconnect, explicit quit, and verified physical-display restoration.

## Last Verified Checkpoint

- The current worktree passes `dotnet format --verify-no-changes`, a warning-as-error solution build,
  and the complete affected suites: 166 Core, 259 Windows Platform, and 187 Server tests.
- Every validation command ended by invoking `restore-physical` and proving the physical panel at
  `2560x1600@240`, primary, with mirror mode disabled, no virtual output, zero HostAgent leases, and
  no active lease heartbeat.
- The guarded production evidence in
  `.artifacts/gate5-production-fdff6aec04c8479a9d4c3efeff3b083c/` retains the prepared snapshot,
  failure snapshot, Android logcat, SessionProbe evidence, capture probes, Server output, and display
  guard log.
- The prepared snapshot proves the physical panel remained primary at `2560x1600@240` while the
  per-client virtual display was extended at `2560x1600@120`. The active failure snapshot proves the
  same virtual display became primary while the physical panel remained extended and mirror mode
  remained disabled.
- The run proves authenticated H.264 SDR media at `1280x720@60`, 12 moving-frame variants, repeated
  emulator `RenderedFrame` feedback, `input-forwarded`, and F12 recorded by the launched SessionProbe.
  Session-owned input targeting therefore closes the prior wrong-foreground failure.
- Input dispatch now resolves the requested session ownership record, verifies the client and display,
  selects only a visible owned window intersecting the leased display, activates that exact window,
  and refuses injection when activation cannot be verified. Diagnostics expose a sanitized result code
  without leaking raw worker errors.
- Gate 5 now arms a separate display-guard process before display preparation. The guard monitors
  acceptance exit, successful completion, power resume, Windows-session unlock, and the global
  `Ctrl+Alt+Shift+F12` emergency action. Recovery forces the internal output, performs exact lease
  cleanup, and does not exit until it proves physical-only primary topology, mirror mode disabled,
  zero leases, and no heartbeat.
- The PowerShell entry point independently repeats and verifies physical restoration in `finally`.
  Cleanup is idempotent when the guard already removed the exact per-run lease.
- The run stopped on a planning-model contradiction: the prepared virtual display correctly remained
  `2560x1600`, but the immutable display plan had been independently clamped to the benchmark-certified
  `1280x720` stream mode. Display geometry and stream output are now separate plan fields. Worker
  preparation and the APK connection grant consume the certified stream dimensions, while display
  lifecycle continues to consume the registered per-client geometry.

## Current Validation Constraint

R1 is still incomplete. The latest guarded run proved media and session-owned input, but validation
stopped before reconnect, explicit quit, the owned process exit, and the restored snapshot. No
topology-changing production run may be used as evidence unless all of the following are true:

1. The owning Windows session is unlocked and its input desktop is `Default`.
2. The runner emits `BEACON_GATE5_DISPLAY_GUARD_ARMED` before `prepared-display`.
3. The run ends with both guard recovery evidence and
   `BEACON_MANDATORY_POST_TEST_RESTORE_END ... topology=True agent=True`.
4. An independent final status proves physical primary, mirror mode disabled, and zero leases.

The guard deliberately does not use a cancellation timeout. Process exit, completion, resume, unlock,
or the emergency hotkey are explicit recovery gates, and failed cleanup is retried only on a monitoring
heartbeat until the final state is proven. The latest failure exercised normal completion-triggered
guard recovery and independently verified physical-only topology and zero leases.

## Hypothesis Under Test

Media, APK rendering, and session-owned F12 input are now retained facts. The next falsifiable question
is whether separating client display geometry from benchmark-certified stream output lets the same
transaction pass active-state validation and continue through fresh-ticket reconnect, explicit quit,
owned-process exit, and restored-state verification. Independently, every outcome must continue to
prove that the external guard and outer runner restore physical-only topology.

## Next Falsifiable Proof

With the owning Windows session on the `Default` desktop, run only the guarded production entry point:

```powershell
.\scripts\test-gate5-production-session.ps1 -Serial emulator-5554 -ArtifactsReady
```

Success requires one retained evidence set proving:

- prepared per-client extended display;
- authenticated media with moving frames and APK render feedback;
- F12 received by the launched SessionProbe;
- active disconnect without premature session destruction;
- fresh-ticket reconnect and explicit quit;
- owned application exit;
- restored server snapshot and released virtual display;
- guard recovery plus outer physical-only, mirror-off, zero-lease verification.

Any earlier failure becomes the next R1 blocker. It does not authorize product-feature work.

## Prior Evidence

- `.artifacts/gate5-production-87104c13eaae4417953cd141bf2e3aef/` records the earlier transient
  `GraphicsCaptureItem::TryCreateFromDisplayId` failure (`0x80070490`) after primary activation. A
  standalone DisplayId probe and WGC probe subsequently succeeded against the same virtual output.
- `.artifacts/gate5-production-5c4d63e57e1e4069802495aa25559207/` records locked-session display
  preparation failing closed with HTTP 503 while cleanup retained physical-only topology.
