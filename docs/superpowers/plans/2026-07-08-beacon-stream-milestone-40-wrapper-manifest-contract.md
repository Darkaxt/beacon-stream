# Milestone 40 - Wrapper Manifest Contract

## Objective

Pin the external streaming wrapper manifest as a documented, machine-checked contract so future Sunshine-compatible wrapper work has a stable adapter boundary instead of relying on scattered tests.

## Requirements Covered

- `REQ-SYNC-001`: Follow implement, validate, sync, refactor, validate, sync.
- `REQ-STREAM-004`: Keep the real streaming backend behind an explicit external-process boundary.
- `REQ-REC-008`: Keep streaming backend capability and failure diagnostics visible.
- `REQ-HDR-006`: Report HDR capability truthfully through the wrapper manifest without forcing unstable HDR behavior.
- `REQ-TEST-001`: Keep the contract testable without a phone.

## Implementation Plan

1. Add a failing test that parses the checked example manifest with the production Windows manifest reader.
2. Add the checked example manifest under `docs/examples`.
3. Document fields, precedence, configuration, and stability rules in `docs/external-streaming-wrapper-manifest.md`.
4. Link the contract from README and the extraction map.
5. Validate focused tests, full static/dynamic checks, and sync through GitHub.

## Non-Goals

- No real Sunshine/GameStream protocol implementation.
- No wrapper log parsing.
- No polling watchdog or timeout-based manifest behavior.
- No HDR driver work in this milestone.
