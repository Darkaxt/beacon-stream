# Inactive Disconnect Compensation Validation

Date: 2026-07-14

## Scope

This Gate 5 compensation audit covers the explicit `clientActive=false` disconnect path. Active
disconnect remains reconnectable and continues to retain the server-owned runtime and display.

## Defect And Regression

An inactive disconnect could stop the runtime and remove the display while leaving the original
unconsumed stream ticket authorized. The first API regression launched a session, disconnected it
as inactive, and then successfully consumed that stale ticket, proving the missing compensation.

Inactive disconnect now performs the transaction in this order:

1. read the server-owned session and work snapshot;
2. stop the runtime when no owned work requires it to remain;
3. revoke unused session tickets in the Server and StreamWorker;
4. evaluate physical restore and display removal under inactive **AND** no-owned-work; and
5. clear ownership only after successful display removal.

A revocation failure returns `503` before physical restore or display removal. Default and active
disconnect do not revoke the reconnect path; reconnect continues to issue a fresh ticket without
restarting the retained runtime or reactivating the display.

## Validation

- Focused API tests prove stale-ticket rejection, revocation-failure compensation, retained active
  reconnect, inactive cleanup, and inactive owned-work retention.
- .NET formatting and warning-as-error build: passed with zero warnings.
- Full managed solution: 514 tests passed.
- Architecture absence, secret fixtures, and Kestrel parser fixtures: passed.
- Client Lab lint and 16 tests: passed.
- Client Lab Playwright disconnect/reconnect/quit lifecycle: passed.
- `git diff --check`: passed.

The changed files are limited to the Server endpoint, Server API tests, and this evidence. The
immediately preceding clean Android 118-task matrix, 23 native Windows tests, and real Worker
process proof remain current for unchanged code. No ADB, emulator, display-topology, or external
streaming installation action occurred.
