# Milestone 46 - No-Phone Pointer Gesture

## Goal

Make the no-phone simulators exercise a realistic pointer gesture after launch instead of only a fixed center tap.

## Requirements Covered

- `REQ-TEST-001`: Most development and validation must not require the real phone.
- `REQ-TEST-003`: Client Lab must simulate launch, input, disconnect, reconnect, quit, and emergency restore flows.
- `REQ-TEST-006`: A CLI fake endpoint must exist for automated tests and scripted sequences.
- `REQ-TEST-010`: Real phone testing remains final confirmation for touch feel and actual stream quality.

## Implementation

1. Add failing Client Lab tests for a deterministic down/move/up pointer gesture payload.
2. Add failing Playwright coverage asserting Client Lab posts the same gesture to `/clients/{clientId}/input`.
3. Add failing fake endpoint tests asserting the CLI script sends down/move/up after launch.
4. Replace the simulator tap payload with the deterministic gesture.
5. Update README documentation.

## Scope Boundaries

- Simulator gesture only.
- No server protocol expansion.
- No new input event types.
- No native touch, multitouch, keyboard, or controller work.
- No timeout-based input behavior.

## Validation

Focused validation:

```powershell
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright test
dotnet test tests\Beacon.FakeEndpoint.Tests\Beacon.FakeEndpoint.Tests.csproj --filter FakeEndpointRunnerTests
```

Full validation is the standard Beacon loop: format, diff check, timeout-pattern audit, .NET build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status.
