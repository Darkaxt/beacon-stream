# Milestone 66: Wrapper Child Config Invariant

Make wrapper child configuration internally consistent: child arguments are valid only when a child executable path is configured.

## Requirements

- `REQ-NET-008`: missing or invalid wrapper child configuration must fail streaming preflight before display or app side effects.
- `REQ-NET-009`: admin and Cockpit streaming health must expose wrapper child readiness.
- `REQ-NET-010`: wrapper child arguments without a wrapper child executable path must be treated as invalid configuration, not as a ready backend.
- `REQ-REC-009`: root fixes and safe auto-repair are preferred over nicer error messages alone.
- `REQ-SYNC-006`: docs, implementation, and validation status must stay aligned.

## Implementation

- Report arguments-only wrapper child configuration as not ready in external-process streaming health.
- Fail streaming preflight when `WrapperChildArguments` is configured without `WrapperChildExecutablePath`.
- Avoid passing `BEACON_WRAPPER_CHILD_ARGUMENTS` to a wrapper command unless `BEACON_WRAPPER_CHILD_EXECUTABLE` is also present.

## Validation

- Focused platform tests cover health, preflight, and command environment behavior.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
