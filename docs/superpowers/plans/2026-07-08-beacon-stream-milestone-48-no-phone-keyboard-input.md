# Milestone 48 - No-Phone Keyboard Input

## Goal

Make no-phone simulators exercise keyboard input after Milestone 47 added the Windows keyboard sink.

## Requirements Covered

- `REQ-TEST-001`: Most development and validation must not require the real phone.
- `REQ-TEST-003`: Client Lab must simulate launch and input flows.
- `REQ-TEST-006`: A CLI fake endpoint must exist for automated tests and scripted sequences.
- `REQ-TEST-010`: Real phone testing remains final confirmation for actual input feel.

## Implementation

1. Add failing Client Lab tests for a deterministic `keyboard` / `press` / `Escape` payload.
2. Add failing Playwright coverage for a `Send Keyboard` action that posts the Escape press.
3. Add failing fake endpoint coverage requiring the scripted input batch to include the Escape press.
4. Implement the Client Lab payload helper and UI button.
5. Add the keyboard event to the fake endpoint scripted input batch.
6. Update README documentation.

## Scope Boundaries

- Simulator exercise only.
- No server protocol expansion beyond the existing `keyboard` event contract.
- No Android keyboard UI.
- No controller, native touch, multitouch, text composition, IME, or GameStream-native input.
- No timeout-based behavior.

## Validation

Focused validation:

```powershell
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright test
dotnet test tests\Beacon.FakeEndpoint.Tests\Beacon.FakeEndpoint.Tests.csproj --filter FakeEndpointRunnerTests
```

Full validation is the standard Beacon loop: format, diff check, timeout-pattern audit, .NET build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status.
