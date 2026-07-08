# Milestone 63: Wrapper Child Configuration

Make the external-process streaming backend pass server-owned child-process configuration into a wrapper harness such as `Beacon.StreamingProbe`.

## Requirements

- `REQ-NET-007`: child wrapper evidence must not be published when the configured child executable is unavailable.
- `REQ-NET-008`: server-owned external-process configuration must be able to pass a wrapper child executable and arguments into the wrapper, and missing configured child executables must fail streaming preflight before display or app side effects.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-SYNC-006`: docs, implementation, and validation status must stay aligned.

## Implementation

- Add `ExternalProcessStreamingOptions.WrapperChildExecutablePath`.
- Add `ExternalProcessStreamingOptions.WrapperChildArguments`.
- Pass child configuration to wrappers as `BEACON_WRAPPER_CHILD_EXECUTABLE` and `BEACON_WRAPPER_CHILD_ARGUMENTS`.
- Fail health/readiness when a configured wrapper child executable is missing.
- Bind server configuration from:
  - `Beacon:Streaming:ExternalProcess:Wrapper:ChildExecutablePath`
  - `Beacon:Streaming:ExternalProcess:Wrapper:ChildArguments`
  - `BEACON_EXTERNAL_STREAMING_WRAPPER_CHILD_EXECUTABLE`
  - `BEACON_EXTERNAL_STREAMING_WRAPPER_CHILD_ARGUMENTS`

## Validation

- Focused platform tests cover child command environment handoff and missing-child health/preflight failure.
- Focused server tests cover configuration and environment override binding.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
