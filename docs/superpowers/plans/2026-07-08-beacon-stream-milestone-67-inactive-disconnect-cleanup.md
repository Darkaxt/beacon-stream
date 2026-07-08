# Milestone 67: Inactive Disconnect Cleanup

Make `/clients/{clientId}/disconnect` distinguish a normal stream disconnect from an explicit "this client is no longer active" disconnect.

Default, empty, or no-body disconnect requests still mean the client may reconnect, so Beacon stops the stream and retains the leased display. When the request explicitly reports `clientActive: false`, Beacon stops the stream, reads the server-owned session ownership snapshot, and evaluates the same cleanup gate used by quit: remove the virtual display only when the client is inactive and no owned process, child process, or tracked window remains.

## Requirements

- `REQ-DISP-005`: disconnect alone must not imply virtual display teardown.
- `REQ-DISP-006`: stream stop and display cleanup are separate operations.
- `REQ-DISP-007`: display removal requires `client inactive AND no owned work remains`.
- `REQ-DISP-008`: no timeout replaces the explicit cleanup rule.
- `REQ-DISP-014`: physical-primary restore remains part of display cleanup/recovery.
- `REQ-DISP-016`: the laptop panel must not remain stranded when no session owns that state.
- `REQ-DISP-017`: a leased virtual display can stay present while owned work still runs there.
- `REQ-SESS-006`: disconnect/reconnect without a new app must not churn displays.
- `REQ-SESS-007`: launch-app, close-app, disconnect can restore/remove once owned work is gone.
- `REQ-SESS-009`: an explicit inactive-client disconnect evaluates the server-owned cleanup gate; default disconnect remains active-client and retains the lease.
- `REQ-REC-007`: recovery tooling must not substitute for correct lifecycle behavior.
- `REQ-SYNC-006`: docs, implementation, and validation status stay aligned.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-TEST-007`: ownership and display cleanup remain interface-backed and fakeable.
- `REQ-TEST-008`: fast tests validate cleanup rules.

## Implementation

- Accept an optional JSON body for `/clients/{clientId}/disconnect`.
- Treat no body, empty body, and `{}` as `clientActive: true`.
- Parse explicit JSON disconnect bodies with web JSON casing so `{ "clientActive": false }` is honored.
- Stop the stream first and keep the existing stop-failure contract.
- When `clientActive` is false, read `ISessionOwnershipTracker` for the session and call `DisplayLeaseManager.CleanupIfAllowedAsync`.
- Pass the cleanup gate as the strict owned-work OR of launched process, child process, or owned tracked window.
- Clear ownership only when the display is actually removed.
- Keep Android, Client Lab, and fake endpoint empty disconnects compatible with the active-client default.

## Validation

- Focused server tests cover:
  - empty-body disconnect retains the lease and stops the stream.
  - explicit inactive disconnect removes the display when no owned work remains.
  - explicit inactive disconnect retains the display when server-owned work remains.
  - raw JSON disconnect bodies still parse `clientActive`.
  - existing empty-object disconnect behavior remains retained.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/build/Playwright, Android test/assemble, and display probe status before merging.
