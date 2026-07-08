# Milestone 65: Wrapper Child Health Diagnostics

Expose configured wrapper child readiness as structured streaming health instead of only folding it into a diagnostic string.

## Requirements

- `REQ-NET-008`: server-owned wrapper child configuration must fail preflight before display or app side effects when the child executable is missing.
- `REQ-NET-009`: admin and Cockpit streaming health must expose wrapper child executable readiness and whether child arguments are configured.
- `REQ-REC-010`: admin diagnostics must make wrapper/client handoff mistakes visible before phone testing.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-SYNC-006`: docs, implementation, and validation status must stay aligned.

## Implementation

- Add wrapper child executable/configuration fields to `StreamingBackendHealth`.
- Populate those fields from `ExternalProcessStreamingBackend.GetHealthAsync`.
- Preserve fake and unknown backend defaults as no child executable configured.
- Include the fields in `/admin/snapshot` JSON.
- Parse and summarize the fields in Cockpit streaming health.

## Validation

- Focused platform tests cover present and missing wrapper child executables.
- Focused server test covers `/admin/snapshot` JSON fields.
- Focused Cockpit tests cover JSON parsing and dashboard summary.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
