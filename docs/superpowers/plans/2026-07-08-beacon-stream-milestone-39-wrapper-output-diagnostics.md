# Milestone 39 - Wrapper Output Diagnostics

## Objective

Capture bounded stdout/stderr diagnostics from external streaming wrappers so process start, exit, and health reports can explain wrapper-side failures without Beacon parsing wrapper-specific log formats.

## Requirements Covered

- `REQ-SYNC-001`: Follow implement, validate, sync, refactor, validate, sync.
- `REQ-REC-008`: Diagnostics must expose stream backend failures.
- `REQ-HDR-006`: Keep streaming backend capability reporting truthful when wrappers explain missing HDR or encoder support.
- `REQ-TEST-001`: Keep validation possible without a phone.

## Implementation Plan

1. Add failing tests proving exited wrapper status includes bounded stdout/stderr diagnostics.
2. Extend `ExternalStreamingProcessStatus` with a diagnostics collection.
3. Capture process stdout/stderr in the Windows runner with bounded in-memory tails.
4. Surface those diagnostics through external backend health/session reconciliation.
5. Keep the capture passive and event/read based; no polling loop, watchdog, cancellation timeout, or restart policy.
6. Validate focused tests, full static/dynamic checks, and sync through GitHub.

## Non-Goals

- No wrapper log parsing.
- No real streaming protocol implementation.
- No background recovery or restart behavior.
