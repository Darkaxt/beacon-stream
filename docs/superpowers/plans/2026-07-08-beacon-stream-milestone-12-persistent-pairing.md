# Beacon Stream Milestone 12: Persistent Profiles And Pairing

## Objective

Make the server's client registry durable and stop treating arbitrary client ids as trusted. The Z Fold 7 profile must remain the seeded first profile, but new clients should only register through an explicit pairing boundary. Client Lab and server tests must be able to validate this without a phone.

## Requirements Covered

- `REQ-PROJ-002`: Keep the Z Fold 7 as the first known endpoint while supporting more registered clients.
- `REQ-CTRL-003`: Let a client update its own basic server-side profile before connection.
- `REQ-CTRL-011`: Keep APK profile patches constrained to the existing allowlist.
- `REQ-PROFILE-001`: Each registered client has a server-side profile.
- `REQ-PROFILE-002`: Persist the basic profile fields used by planning.
- `REQ-PROFILE-003`: Preserve the Z Fold 7 `2560x1600` and `120 Hz` default.
- `REQ-PROFILE-004`: Never silently collapse the `2560x1600` profile to `2560x1440`.
- `REQ-TEST-003`: Client Lab must simulate hello, profile fetch, allowed patching, and launch prerequisites without a phone.
- `REQ-TEST-004`: Client Lab must keep the Z Fold 7 profile.
- `REQ-TEST-008`: Fast tests must validate profile persistence and validation.

## Implementation Plan

1. Add failing tests for the pairing/profile behavior:
   - known Z Fold 7 hello still works without a pairing token;
   - unknown clients are rejected without a valid pairing token;
   - unknown clients with a valid token are registered with a durable default profile;
   - saved profile edits reload from disk without losing the `2560x1600` intent.
2. Introduce a small profile repository boundary:
   - keep live capabilities and telemetry in memory;
   - persist only registered client profiles;
   - seed the Z Fold 7 profile if the store is empty.
3. Add explicit pairing configuration:
   - support a configured pairing token for registering a new client;
   - keep the default developer/test path deterministic;
   - expose pairing state in admin diagnostics without exposing the token.
4. Update `/clients/hello`:
   - existing known clients may hello without pairing;
   - unknown clients need a valid token;
   - registered profile names may be initialized from the hello payload.
5. Update Client Lab/fake endpoint request shapes if needed so simulation can exercise known-client and pairing flows.
6. Document configuration, behavior, and test commands.

## Non-Goals

- Native Sunshine/Moonlight streaming protocol work.
- Android media decode/input implementation.
- New WPF profile editing screens.
- External identity provider or multi-user auth.
- Time-based pairing expiry.

## Validation

- `dotnet format Beacon.slnx --verify-no-changes`
- `dotnet build Beacon.slnx -warnaserror`
- `dotnet test Beacon.slnx`
- `pnpm --dir src\Beacon.ClientLab lint`
- `pnpm --dir src\Beacon.ClientLab test`
- `pnpm --dir tests\Beacon.ClientLab.Playwright lint`
- `pnpm --dir tests\Beacon.ClientLab.Playwright test`
- `gradle -p src\Beacon.Android test assembleDebug`
- `rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n`

## Sync Plan

Commit the plan first, then implement and validate in a second commit. Open a draft PR immediately after the implementation branch has meaningful tests, then mark ready only after static and dynamic checks pass locally and in GitHub Actions.
