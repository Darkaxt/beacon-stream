# Milestone 61: Android Connection Diagnostics

Teach the thin Android APK to parse the full stream connection descriptor and report a clear diagnostic when the server provides endpoint metadata without a launch URI.

## Requirements

- `REQ-CTRL-013`: endpoint-only launch responses must be visible client diagnostics until native endpoint streaming exists.
- `REQ-CTRL-009`: the client consumes the server plan and must not reinterpret topology or stream policy locally.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-SYNC-006`: docs, implementation, and validation status must stay aligned.

## Implementation

- Add `StreamConnectionDescriptor` for APK-side parsing of `stream.connection.protocol`, `launchUri`, and endpoint role/URI pairs.
- Keep `StreamConnectionLaunchUri` as a compatibility helper over the descriptor parser.
- On successful launch, delegate `ACTION_VIEW` only when `launchUri` is present.
- When the connection is endpoint-only, record a visible `latestError` diagnostic with protocol and endpoint summary.
- Include `latestError` in the activity status text.

## Validation

- Focused Android tests:
  - `StreamConnectionLaunchUriTest`
  - `BeaconViewModelTest`
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
