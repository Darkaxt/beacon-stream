# Milestone 55: Fake Endpoint Stream Assertion

Extend the no-phone fake endpoint script with an opt-in stream connection assertion so scripted validation can fail when a launched session does not expose a usable `stream.connection` descriptor.

## Requirements

- `REQ-TEST-001`: most development and validation must not require the real phone.
- `REQ-TEST-006`: the CLI fake endpoint must support automated tests and scripted sequences.
- `REQ-TEST-007`: streaming remains interface-backed and fakeable.
- `REQ-CTRL-009`: the client consumes the server-provided stream descriptor instead of interpreting stream policy locally.
- `REQ-REC-008`: stream handoff failures remain observable as explicit diagnostics.

## Implementation

- Add `RequireStreamConnection` to `FakeEndpointScript`.
- Add `--require-stream-connection true` to the fake endpoint CLI.
- Keep the default script unchanged so existing fake-backend validation remains stable.
- When enabled, call `GET /clients/{clientId}/stream` immediately after launch and before input.
- Require `stream.connection.protocol` plus either `launchUri` or at least one endpoint.
- Record `stream connection <protocol>` in the script operations on success.
- Fail with a specific `stream.connection` error and stop the remaining script operations when the descriptor is absent or unusable.

## Validation

- Focused tests cover command-line parsing, successful stream descriptor validation, and missing-descriptor failure.
- Full validation must include format, diff check, timeout-pattern audit, .NET build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
