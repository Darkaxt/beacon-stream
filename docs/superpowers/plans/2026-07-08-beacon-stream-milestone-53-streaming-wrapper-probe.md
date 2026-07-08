# Milestone 53: Streaming Wrapper Probe

Add a tiny no-phone external streaming wrapper probe so Beacon can exercise the external-process runtime descriptor boundary against a real executable before a Sunshine-compatible wrapper exists.

## Requirements

- `REQ-TEST-001`: most development and validation must not require the real phone.
- `REQ-TEST-006`: scripted endpoints and probes must support automated validation.
- `REQ-TEST-007`: streaming remains interface-backed and fakeable.
- `REQ-STREAM-004`: real streaming work stays behind an explicit wrapper boundary.
- `REQ-REC-008`: stream handoff state remains observable through descriptor output and backend diagnostics.

## Implementation

- Add `Beacon.StreamingProbe`, a Windows console executable.
- Parse the same arguments and environment variables passed by `ExternalProcessStreamingBackend`.
- Write an `ExternalStreamingSessionDescriptor` JSON file at `BEACON_STREAM_SESSION_DESCRIPTOR_PATH` / `--stream-session-descriptor`.
- Use deterministic loopback defaults when explicit connection metadata is absent.
- Support `--once` for standalone descriptor validation; normal wrapper mode writes the descriptor and waits until Beacon stops the process.
- Add focused tests for argument/environment parsing, descriptor writing, and wait-vs-once behavior.

## Validation

- New probe tests cover command parsing, descriptor JSON compatibility, and process-lifetime mode selection.
- Full validation must include format, build, test, ClientLab, Playwright, Android, the standalone probe command, and display probe before merging.
