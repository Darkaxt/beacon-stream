# Milestone 54: External Wrapper API Smoke

Add a no-phone API smoke path that launches the real `Beacon.StreamingProbe` executable through the real Windows external-process runner while keeping display and game launch side effects fake.

## Requirements

- `REQ-TEST-001`: most development and validation must not require the real phone.
- `REQ-TEST-007`: display, streaming, game launch, and process boundaries remain interface-backed and fakeable.
- `REQ-STREAM-004`: real streaming work stays behind an explicit wrapper boundary.
- `REQ-DISP-006`: stream stop and display cleanup remain separate operations.
- `REQ-REC-008`: stream backend failures and handoff state remain observable.

## Implementation

- Add a server API integration test that swaps only `IStreamingBackend` to `ExternalProcessStreamingBackend`.
- Use `WindowsExternalStreamingProcessRunner` so the test launches a real child process.
- Use `Beacon.StreamingProbe.exe` as the wrapper and `WindowsExternalStreamingSessionDescriptorStore` as the runtime descriptor boundary.
- Keep fake display and fake game launch boundaries so the test cannot change Windows display topology or launch a real game.
- Wait on the descriptor file creation event in the test harness before reading `/clients/{clientId}/stream`; the backend remains asynchronous and does not block launch waiting for wrapper metadata.
- Assert that `/disconnect` stops the owned wrapper process and removes the runtime descriptor.

## Validation

- The focused server API test proves launch, runtime descriptor readback, and stop behavior without a phone.
- Full validation must include format, diff check, timeout-pattern audit, .NET build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
