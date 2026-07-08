# Milestone 36 - Display Health Snapshot

## Objective

Expose read-only display backend health through the admin snapshot and Cockpit so driver readiness, mirror mode, physical-primary verification, and active display paths are visible before a launch fails or leaves the host in a bad topology.

## Requirements Covered

- `REQ-SYNC-001`: Follow implement, validate, sync, refactor, validate, sync.
- `REQ-VDISP-014`: Surface truthful display capability and topology diagnostics.
- `REQ-OPS-006`: Local administration must expose enough state to recover without guessing.
- `REQ-TEST-001`: Keep the slice testable without a real phone.

## Implementation Plan

1. Add failing tests for `/admin/snapshot`, Windows display backend health, and Cockpit parsing/status rendering.
2. Add a read-only `DisplayHealth` contract to the display backend abstraction.
3. Implement fake and Windows health snapshots without creating, removing, or changing display topology.
4. Include display health in `/admin/snapshot`.
5. Parse and render display health in Cockpit diagnostics and status.
6. Validate focused tests, full static/dynamic checks, and the real read-only display probe.

## Non-Goals

- No display creation or restore behavior changes.
- No HDR capability changes.
- No Android behavior changes.
