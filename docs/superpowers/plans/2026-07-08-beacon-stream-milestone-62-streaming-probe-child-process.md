# Milestone 62: Streaming Probe Child Process

Make the no-phone streaming probe able to supervise a real child streaming process, so Beacon can validate wrapper ownership before a Sunshine-compatible integration exists.

## Requirements

- `REQ-NET-007`: a wrapper harness that supervises a child streaming process must fail before publishing runtime descriptor evidence when the child executable is unavailable, and it must exit when the child exits before Beacon stops the wrapper.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-TEST-006`: scripted endpoints and probes must support automated validation.
- `REQ-SYNC-006`: docs, implementation, and validation status must stay aligned.

## Implementation

- Add `--child-executable` / `BEACON_WRAPPER_CHILD_EXECUTABLE`.
- Add `--child-arguments` / `BEACON_WRAPPER_CHILD_ARGUMENTS`.
- Pass normalized Beacon session/display/connection environment through to the child process.
- Start the child before writing the runtime session descriptor.
- Do not write the descriptor if the configured child executable is missing.
- In normal mode, exit with the child exit code if the child exits before Beacon stops the wrapper.
- Stop the child when Beacon stops the wrapper or when `--once` validation exits.

## Validation

- Focused probe tests cover child argument parsing, child start/stop ownership, missing-child false-evidence prevention, and child-exit propagation.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
