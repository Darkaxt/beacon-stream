# Beacon Release Blocker

Updated: 2026-08-02

## Active Outcome

R1 Integrated Streaming Proof: complete one guarded emulator-backed production transaction through
session-owned input, reconnect, explicit quit, and verified physical-display restoration.

## Last Verified Checkpoint

- The current worktree passes `dotnet format --verify-no-changes`, a warning-as-error solution build,
  and the complete affected suites: 166 Core, 259 Windows Platform, and 186 Server tests.
- Every validation command ended by invoking `restore-physical` and proving physical `DISPLAY1` at
  `2560x1600@240`, primary, with mirror mode disabled, no virtual output, zero HostAgent leases, and
  no active lease heartbeat.
- Retained prepared evidence in
  `.artifacts/gate5-production-a528253ef29a42e3b9f7969ae8e78613/prepared-snapshot.json` proves
  physical `DISPLAY1` remained primary at `2560x1600@240` while the per-client virtual display was
  extended at `2560x1600@120` before launch.
- The interrupted production run reached authenticated H.264 SDR media at `1280x720@60`, changing
  frames, and emulator `RenderedFrame` feedback. Those later observations were visible in the live
  runner output but were not fully retained after the laptop was hibernated, so they are diagnostic
  evidence and not an accepted R1 proof.
- That run reported input forwarding, but the SessionProbe never recorded F12. Helium was the
  unrelated foreground window, proving that foreground-global `SendInput` dispatch did not establish
  a session-owned target.
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

## Current Validation Constraint

R1 is still incomplete. The interrupted run did not produce retained F12, reconnect, quit, restored
snapshot, and cleanup evidence in one transaction. No topology-changing production run may be used as
evidence unless all of the following are true:

1. The owning Windows session is unlocked and its input desktop is `Default`.
2. The runner emits `BEACON_GATE5_DISPLAY_GUARD_ARMED` before `prepared-display`.
3. The run ends with both guard recovery evidence and
   `BEACON_MANDATORY_POST_TEST_RESTORE_END ... topology=True agent=True`.
4. An independent final status proves physical primary, mirror mode disabled, and zero leases.

The previous runner could wait indefinitely for F12 while the virtual display remained primary. The
new guard deliberately does not use a cancellation timeout. Process exit, completion, resume, unlock,
or the emergency hotkey are explicit recovery gates, and failed cleanup is retried only on a monitoring
heartbeat until the final state is proven.

## Hypothesis Under Test

The media path is far enough along to expose the first input event. The next falsifiable question is
whether activating the verified session-owned window before `SendInput` produces retained F12 evidence
without targeting an unrelated local window. Independently, every outcome must prove that the external
guard and outer runner restore physical-only topology.

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
