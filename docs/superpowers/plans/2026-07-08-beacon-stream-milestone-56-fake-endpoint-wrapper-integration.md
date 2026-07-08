# Milestone 56: Fake Endpoint Wrapper Integration

Exercise the no-phone fake endpoint against the real server API and the real `Beacon.StreamingProbe` external-process wrapper. This closes the gap between the server-only wrapper smoke test and the fake-endpoint-only stream assertion.

## Requirements

- `REQ-TEST-001`: most development and validation must not require the real phone.
- `REQ-TEST-006`: the CLI fake endpoint must support automated tests and scripted sequences.
- `REQ-TEST-007`: display, streaming, input, and process boundaries must stay interface-backed and fakeable.
- `REQ-CTRL-009`: the client consumes the server-provided stream descriptor instead of interpreting stream policy locally.
- `REQ-REC-008`: stream handoff and wrapper cleanup remain observable.

## Reference Check

Sunshine/Moonlight-compatible hosts expose a known connection surface from configuration, including the port family where RTSP defaults to `48010` as documented in Sunshine advanced usage: <https://docs.lizardbyte.dev/projects/sunshine/v0.23.0/about/advanced_usage.html#port>

Beacon follows that pattern at the wrapper boundary: static manifest or explicit connection metadata gives the client an immediate descriptor. Runtime session descriptors are wrapper evidence and may override that metadata when present, but Beacon does not add a timeout-based launch wait for the descriptor file.

## Implementation

- Add `EndAfterStreamConnection` to `FakeEndpointScript`.
- Add `--end-after-stream-connection true` to the fake endpoint CLI.
- Keep the default fake endpoint script unchanged.
- When enabled, the fake endpoint stops its script immediately after the required stream connection assertion succeeds, leaving the running stream available for integration tests.
- Add a server integration test that:
  - replaces only the streaming backend with `ExternalProcessStreamingBackend`;
  - launches the real `Beacon.StreamingProbe` through `WindowsExternalStreamingProcessRunner`;
  - configures immediate GameStream-style connection metadata;
  - runs `FakeEndpointRunner` against `WebApplicationFactory<Program>`;
  - verifies the fake endpoint receives `stream connection gamestream`;
  - waits for the runtime descriptor through the existing file-system event helper;
  - verifies the runtime descriptor metadata names `Beacon.StreamingProbe`;
  - disconnects and verifies generated descriptor cleanup.

## Validation

- Focused fake endpoint tests cover command-line parsing and ending after stream connection.
- Focused server test covers fake endpoint plus real wrapper probe integration.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
