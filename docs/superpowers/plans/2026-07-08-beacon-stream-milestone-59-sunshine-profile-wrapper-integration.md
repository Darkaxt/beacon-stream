# Milestone 59: Sunshine Profile Wrapper Integration

Add no-phone integration coverage proving a server-configured Sunshine endpoint profile reaches the external wrapper boundary and returns through the fake endpoint stream connection contract.

## Requirements

- `REQ-NET-006`: derived Sunshine/GameStream endpoint metadata must work through the server-owned connection path.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-TEST-006`: CLI fake endpoint must cover scripted control-plane sequences.
- `REQ-TEST-007`: display, streaming, and process boundaries stay fakeable for deterministic tests.
- `REQ-SYNC-006`: docs, implementation, and validation status must stay aligned.

## Implementation

- Add `FakeEndpointScriptCompletesAgainstStreamingProbeWithSunshineProfile`.
- Configure the server test backend with only `SunshineEndpointProfile`, not hand-entered connection endpoints.
- Launch the real `Beacon.StreamingProbe` through `WindowsExternalStreamingProcessRunner`.
- Verify the fake endpoint sees `stream connection gamestream`.
- Verify the runtime descriptor contains Sunshine-profile endpoints such as:
  - `rtsp://127.0.0.1:48010`
  - `udp://127.0.0.1:48000`

## Validation

- Focused server test: `FakeEndpointScriptCompletesAgainstStreamingProbeWithSunshineProfile`.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
