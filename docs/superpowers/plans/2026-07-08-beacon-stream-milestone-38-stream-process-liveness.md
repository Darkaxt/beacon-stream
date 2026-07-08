# Milestone 38 - Stream Process Liveness

## Objective

Reconcile external streaming wrapper process liveness on demand so Beacon does not keep reporting a stream as running after its owned wrapper process has exited.

## Requirements Covered

- `REQ-SYNC-001`: Follow implement, validate, sync, refactor, validate, sync.
- `REQ-SESS-001`: A session owns the process launched by the orchestrator.
- `REQ-SESS-006`: Disconnect/reconnect must keep session state coherent.
- `REQ-REC-008`: Diagnostics must expose stream backend failures.
- `REQ-TEST-001`: Keep validation possible without a phone.

## Implementation Plan

1. Add failing tests for external wrapper process status and stream-session reconciliation.
2. Add a read-only process status method to the external process runner boundary.
3. Reconcile process-backed sessions when reading health, all sessions, or one session.
4. Mark exited wrapper-backed sessions as `exited` with a diagnostic error instead of leaving them as `running`.
5. Keep the design event-driven/on-demand; no polling loop, timers, or cancellation timeout behavior.
6. Validate focused tests, full static/dynamic checks, and sync through GitHub.

## Non-Goals

- No background watchdog.
- No restart policy.
- No real streaming protocol changes.
