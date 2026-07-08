# Milestone 60: Streaming Health Endpoints

Expose the static stream endpoint map through backend health and Cockpit diagnostics so connection handoff mistakes can be inspected before phone testing.

## Requirements

- `REQ-REC-010`: admin diagnostics must expose advertised connection protocol, launch URI, and static endpoint map.
- `REQ-NET-006`: derived Sunshine/GameStream endpoint metadata must remain server-owned and visible.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-SYNC-006`: docs, implementation, and validation status must stay aligned.

## Implementation

- Add `StreamingBackendHealth.Endpoints`.
- Have `FakeStreamingBackend` report a deterministic fake control endpoint for health diagnostics.
- Have `ExternalProcessStreamingBackend.GetHealthAsync` report explicit/profile endpoints over manifest endpoints using the same normalization as session descriptors.
- Add `CockpitStreamingHealth.Endpoints`.
- Include endpoint count in the Cockpit streaming health summary.
- Preserve runtime session descriptor precedence for running streams; health endpoints are advertised static backend state only.
- Harden the no-phone server test helper so descriptor readiness waits for readable runtime descriptor JSON instead of file creation alone.

## Validation

- Focused platform test: `GetHealthAsyncReportsSunshineEndpointProfile`.
- Focused admin snapshot test: `SnapshotReturnsClientsGamesAndSessions`.
- Focused Cockpit tests: `LoadsSnapshotFromServer` and `RefreshPopulatesDashboardState`.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
