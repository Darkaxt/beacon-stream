# Milestone 42 - Input Diagnostics

## Objective

Surface client input forwarding decisions in the existing operational diagnostics journal so accepted, rejected, and failed input batches are visible through `/admin/snapshot` and Cockpit diagnostics.

## Requirements Covered

- `REQ-SYNC-001`: Follow implement, validate, sync, refactor, validate, sync.
- `REQ-REC-008`: Logs must expose stream/backend failures and selected operational decisions.
- `REQ-TEST-001`: Keep validation possible without a phone.

## Implementation Plan

1. Add a failing admin snapshot test for successful input forwarding diagnostics.
2. Publish `input/input.forward` diagnostics for accepted and failed forwarding.
3. Publish `input/input.reject` diagnostics for missing session, stopped stream, or empty input payloads.
4. Validate focused tests, full static/dynamic checks, and sync through GitHub.

## Non-Goals

- No native input injection.
- No input batching queue.
- No timeout-based input expiry or cancellation.
