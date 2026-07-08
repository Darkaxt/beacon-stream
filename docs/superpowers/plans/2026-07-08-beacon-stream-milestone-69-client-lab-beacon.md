# Milestone 69: Client Lab Beacon Simulation

Make the browser Client Lab exercise the explicit client beacon lifecycle action added in Milestone 68.

Client Lab is the interactive no-phone remote-client simulator. It should be able to report active beacon before a session and inactive beacon when the simulated client stops beaconing, while keeping all virtual-display policy on Beacon Server.

## Requirements

- `REQ-CTRL-002`: the simulated client reports user intent and activity to the server.
- `REQ-CTRL-007`: virtual desktop behavior remains server-side state.
- `REQ-CTRL-014`: active beacon prepares the display lease and inactive beacon evaluates cleanup without timers or watchdogs.
- `REQ-DISP-002`: a client can have one leased virtual display prepared for that client's lifecycle.
- `REQ-DISP-007`: display removal requires `client inactive AND no owned work remains`.
- `REQ-DISP-008`: no timeout replaces the cleanup rule.
- `REQ-TEST-001`: validation works without a real phone.
- `REQ-TEST-002`: Client Lab simulates a remote client control plane.
- `REQ-TEST-003`: Client Lab simulates hello, profile fetch, allowed profile patch, capability report, telemetry report, active/inactive beacon, plan request, launch request, disconnect, reconnect, quit, and emergency restore.
- `REQ-TEST-005`: Client Lab remains browser-testable with Playwright.
- `REQ-SYNC-006`: docs, implementation, and validation stay aligned.

## Implementation

- Add `BeaconResponse` and `formatBeaconState` to the Client Lab helper module.
- Add Active Beacon and Inactive Beacon buttons to the Session controls.
- `Active Beacon` posts `{ active: true }` to `/clients/z-fold-7/beacon`.
- `Inactive Beacon` posts `{ active: false }` to `/clients/z-fold-7/beacon`.
- Log the server response as `beacon active <displayId> prepared` or `beacon inactive <displayId> removed/retained`.
- Do not let Client Lab send display topology decisions or cleanup booleans; it only reports client activity.

## Validation

- Unit tests cover beacon response formatting.
- Playwright covers both active and inactive beacon requests and log entries.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/build/Playwright, Android test/assemble, and display probe status before merging.
