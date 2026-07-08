# Milestone 68: Client Beacon Lease Preparation

Add an explicit client-beacon control-plane action for the server-owned display lifecycle.

When a registered client reports that it is actively beaconing, Beacon prepares or verifies that client's virtual display lease before any app launch. When the client explicitly reports that it is no longer active, Beacon evaluates the same server-owned cleanup gate as quit and inactive disconnect. This is an event-driven lifecycle action, not a timeout, polling loop, or watchdog.

## Requirements

- `REQ-DISP-001`: virtual display identity remains per client.
- `REQ-DISP-002`: a client can have one leased virtual display prepared for that client's session lifecycle.
- `REQ-DISP-003`: the leased display is created or verified during preflight before app launch.
- `REQ-DISP-005`: disconnect alone does not imply display teardown.
- `REQ-DISP-006`: stream stop and display cleanup are separate operations.
- `REQ-DISP-007`: display removal requires `client inactive AND no owned work remains`.
- `REQ-DISP-008`: no timeout replaces the cleanup rule.
- `REQ-DISP-014`: physical-primary restore remains verified through the display backend.
- `REQ-DISP-017`: owned work can keep the virtual display present after the client goes inactive.
- `REQ-CTRL-014`: active client beacon prepares the display lease and inactive beacon evaluates cleanup without timers.
- `REQ-SESS-006`: reconnect-oriented client activity must not churn displays unnecessarily.
- `REQ-SESS-007`: cleanup can restore/remove only once owned work is gone.
- `REQ-SESS-009`: explicit inactive-client signals use the server-owned cleanup gate.
- `REQ-SYNC-006`: docs, implementation, and validation stay aligned.
- `REQ-TEST-001`: validation works without a real phone.
- `REQ-TEST-007`: lifecycle work remains interface-backed and fakeable.
- `REQ-TEST-008`: fast tests validate cleanup rules.

## Implementation

- Add `POST /clients/{clientId}/beacon`.
- Accept optional `{ "active": true }` or `{ "active": false }`; empty/default requests are active.
- Active beacon:
  - requires a registered profile.
  - calls `DisplayLeaseManager.EnsureLeaseAsync`.
  - returns the prepared display id and state without launching an app or starting a stream.
- Inactive beacon:
  - reads the current session plan when one exists.
  - reads `ISessionOwnershipTracker` only when a session plan exists.
  - calls `DisplayLeaseManager.CleanupIfAllowedAsync` with `clientActive: false` and the server-owned process/window snapshot.
  - clears ownership only when the display is removed.
- Do not add a background timer, timeout, polling watchdog, or implicit app/process termination.
- Make the CLI fake endpoint send an active beacon before plan/launch so the flow is testable without a phone.
- Expose thin Android API/ViewModel methods plus manual APK controls for active and inactive beacon.

## Validation

- Focused server API tests cover active lease preparation, unknown client rejection, inactive cleanup with no owned work, and inactive retention when owned work remains.
- Fake endpoint tests cover the active beacon operation before plan/launch.
- Android JVM tests cover `BeaconApiClient.beacon(active)` serialization and ViewModel forwarding.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/build/Playwright, Android test/assemble, and display probe status before merging.
