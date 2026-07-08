# Milestone 52: Runtime Stream Descriptor Handoff

Add a wrapper runtime descriptor handoff so an external streaming wrapper can publish the actual per-session launch URI and endpoints after it starts, without requiring phone testing or copying Sunshine source.

## Requirements

- `REQ-CTRL-008`: the server keeps the computed plan and owns the stream handoff contract.
- `REQ-CTRL-009`: the client consumes the server-provided stream descriptor and does not reinterpret stream policy.
- `REQ-STREAM-004`: external streaming remains behind an explicit wrapper boundary.
- `REQ-REC-008`: backend state and failures stay visible through diagnostics and health.
- `REQ-TEST-001` and `REQ-TEST-007`: behavior is testable without a phone through interfaces and fake backends.

## Implementation

- Add `ExternalStreamingSessionDescriptor` as a Beacon-owned runtime JSON contract.
- Add an `IExternalStreamingSessionDescriptorStore` abstraction and a Windows file-backed implementation.
- Before wrapper launch, prepare a fresh descriptor path and remove stale same-session JSON.
- Pass the path to the wrapper through `BEACON_STREAM_SESSION_DESCRIPTOR_PATH` and `--stream-session-descriptor`.
- Read descriptor JSON on start and on-demand session/health reads, with runtime descriptor data taking precedence over static manifest connection fields.
- Delete generated descriptor state when the stream stops or the wrapper exits.

## Validation

- Focused platform tests cover start-time descriptor write, stale descriptor clearing, late descriptor refresh, and checked JSON examples.
- Server registration tests verify external-process mode wires the Windows descriptor store.
- Full validation must still include format, build, test, ClientLab, Playwright, Android, and display probe before merging.
