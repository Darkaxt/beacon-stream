# Runtime Authorization And Worker Capability Evidence

Date: 2026-07-14

## Scope

This slice removes Worker wire ownership from Core and Server ticket security,
binds every authorization and revocation to the exact runtime generation that
issued the ticket, and makes production-video capability failures observable
without introducing a synthetic encoder or alternate media route.

## Red Evidence

- `CoreStreamAuthorizationContractIsRuntimeNeutral` failed while Core records
  retained Worker-specific names.
- `ServerTicketOwnershipIsRuntimeNeutral` failed while Server ticket security
  constructed `AuthorizeTicket` protobuf messages directly.
- `AuthorizationUsesTheCapturedRuntimeGeneration`,
  `AuthorizationCannotMoveToAReplacementRuntime`, and
  `RevocationOfARetiredRuntimeDoesNotContactItsReplacement` failed when the
  authorizer used the current Worker process instead of the captured
  generation.
- `StreamWorkerHostExposesOnlyGenerationBoundCommands` failed while the host
  still exposed an unpinned `SendAsync` overload.
- An intentional nonexistent-display process probe exited `92` with no
  diagnostic output even though the Worker had published structured failure
  events.
- `HostedRunnerVideoExceptionIsExplicitAndNarrow` failed while CI treated every
  host as production-video capable.

## Implemented Boundary

- Core exposes only `StreamRuntimeAuthorization*` records.
- `StreamTicketService` stores runtime instance and generation ownership and
  returns a protocol-neutral `StreamTicketAuthorization` record.
- `StreamWorkerSessionAuthorizer` is the mapping boundary from generic runtime
  authorization into Worker protobuf commands.
- Authorization is sent only to the captured generation. A replacement Worker
  cannot inherit a ticket from the retired runtime.
- Revocation is sent only to the issuing generation. If that generation has
  retired, revocation is complete without contacting its replacement.
- `IStreamWorkerHost` exposes one generation-bound command method.
- The native process probe consumes Worker failure state, diagnostic, and
  disconnect events, requests graceful Worker shutdown, and reports the exact
  boundary and native code.
- The standard integration command still requires a real H.264 access unit.
  CI must opt into `-AllowUnsupportedVideoHardware`, which permits only
  `BEACON_WORKER_VIDEO_FAILURE CAPTURE 5` (NVIDIA adapter missing). No other
  failure is accepted and the workflow does not use `continue-on-error`.

The hosted-runner policy matches GitHub's documented distinction between
standard `windows-latest` virtual machines and separately offered GPU-powered
larger runners:
<https://docs.github.com/en/actions/reference/runners/github-hosted-runners>.

## Local Verification

- `dotnet format Beacon.slnx --verify-no-changes --no-restore --verbosity minimal`
- `dotnet build Beacon.slnx -warnaserror --no-restore --verbosity minimal`
- `dotnet test Beacon.slnx --no-build --verbosity minimal`
- `scripts/test-gate3.ps1 -ValidateFixtures`
- `scripts/build-native-windows.ps1`
- `scripts/test-stream-worker-integration.ps1`
- Client Lab lint, unit tests, Playwright lint, and lifecycle test

Results: the managed build completed with zero warnings and zero errors; 518
managed tests passed; all four Gate 3 fixture markers passed; 23 native CTest
tests passed; Client Lab passed 16 unit tests and one Playwright lifecycle test.

The production-capable local host produced:

```text
BEACON_WORKER_IPC_QUIC_OK AUTH INPUT FEEDBACK REAL_H264_ACCESS_UNIT DISCONNECT SHUTDOWN
BEACON_WORKER_VIDEO_FAILURE CAPTURE 2
BEACON_WORKER_BENCHMARK_OK AUTH RELIABLE DATAGRAM RTT DISCONNECT SHUTDOWN
BEACON_WORKER_STARTUP_EXIT 64
```

The `CAPTURE 2` line is the intentional nonexistent-display diagnostic proof;
it is not accepted as production-video success.

No Android device or emulator command, display-topology mutation, display
driver operation, or external streaming installation was used by this slice.

## Sync Evidence

Current-head CI evidence is pending the validated commit and push.
