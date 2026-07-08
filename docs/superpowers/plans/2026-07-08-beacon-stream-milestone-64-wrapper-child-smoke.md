# Milestone 64: Wrapper Child Smoke

Add a server-level no-phone smoke test for the configured wrapper child path.

## Requirements

- `REQ-NET-007`: a child wrapper exit must be visible to the streaming lifecycle.
- `REQ-NET-008`: server-owned wrapper child configuration must reach the wrapper process.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-TEST-006`: scripted endpoints and probes must support automated validation.
- `REQ-SYNC-006`: docs, implementation, and validation status must stay aligned.

## Implementation

- Add a `ClientApiTests` smoke that launches `Beacon.StreamingProbe` through `ExternalProcessStreamingBackend`.
- Configure a real Windows child process through `ExternalProcessStreamingOptions.WrapperChildExecutablePath` and `WrapperChildArguments`.
- Wait for the runtime descriptor evidence.
- Query the stream through the server API and verify the wrapper reconciles to `exited` with the child exit code in the diagnostic.

## Validation

- Focused server smoke: `LaunchWithStreamingProbeChildProcessReportsChildExit`.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
