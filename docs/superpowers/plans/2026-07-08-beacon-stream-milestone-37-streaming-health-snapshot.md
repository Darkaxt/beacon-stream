# Milestone 37 - Streaming Health Snapshot

## Objective

Expose read-only streaming backend health through the admin snapshot and Cockpit so external wrapper configuration, manifest readiness, advertised capabilities, and active stream counts are visible before a launch tries to use the backend.

## Requirements Covered

- `REQ-SYNC-001`: Follow implement, validate, sync, refactor, validate, sync.
- `REQ-REC-008`: Logs and diagnostics must expose stream backend failures.
- `REQ-TEST-001`: Keep validation possible without a phone.
- `REQ-TEST-007`: Keep streaming behind a fakeable interface.
- `REQ-HDR-006`: Represent streaming backend HDR capability truthfully as one part of the HDR chain.

## Implementation Plan

1. Add failing tests for `/admin/snapshot`, the external-process streaming backend, and Cockpit parsing/status rendering.
2. Add a read-only `StreamingBackendHealth` contract to `IStreamingBackend`.
3. Implement fake and external-process health snapshots without starting or stopping streams.
4. Include streaming health in `/admin/snapshot`, with degraded health if the backend throws.
5. Parse and render streaming health in Cockpit diagnostics/status.
6. Validate focused tests, full static/dynamic checks, and sync the branch through GitHub.

## Non-Goals

- No real streamer implementation.
- No Moonlight/GameStream protocol changes.
- No process start/stop behavior changes.
