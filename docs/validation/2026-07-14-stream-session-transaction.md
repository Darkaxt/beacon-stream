# Stream Session Transaction Validation

Date: 2026-07-14

## Scope

Task 19 integrates Beacon-owned launch, transport disconnect, reconnect, explicit stop,
quit, Worker restart, owned-work termination, and display cleanup. Apollo, Sunshine,
GameStream, Moonlight, ADB, the Android emulator, and the live display driver were not
used during this validation.

## Proven Behavior

- Launch order is preflight, display preparation, Worker readiness, display activation,
  application launch, ownership recording, and ticket authorization.
- Result failures, thrown activation or launcher failures, ownership-record failures, and
  ticket cancellation reverse completed stages with generation-scoped Worker cleanup.
- Active disconnect stops APK media resources while retaining the Beacon Worker runtime,
  application ownership, and display lease for a fresh-ticket reconnect.
- Worker loss permits reconnect-driven restart only for a session with recorded launch
  ownership. A plan without a launched session remains unavailable.
- Inactive cleanup changes topology only when the client is inactive **AND** the three
  approved owned-work signals are empty.
- Quit terminates only inspected session-owned process identities before restoring physical
  primary and removing the client display.
- Steam and other shell launches do not claim the shell handler process as the game PID.
- Client Lab and FakeEndpoint exercise disconnect, same-session reconnect, explicit stop,
  quit, and emergency recovery without requesting a replacement plan.

## Evidence

- `dotnet format Beacon.slnx --no-restore --verify-no-changes`: passed.
- `dotnet test Beacon.slnx --no-restore`: 505 passed.
- Android `:app:testDebugUnitTest`: passed.
- Android `:app:assembleDebug :app:compileDebugAndroidTestJavaWithJavac`: passed.
- Client Lab `pnpm test`: 16 passed.
- Client Lab `pnpm build`: passed.
- Native `ctest --preset windows-x64-debug`: 22 passed.
- `scripts/test-quic-listener.ps1`: replay reconnect, media recovery, ordered session
  actions, callback faults, and disconnect faults passed.
- `scripts/test-stream-worker-integration.ps1`: real Beacon Worker IPC/QUIC lifecycle and
  startup-exit isolation passed.
- Post-sync refactor extracted reconnect ownership, Worker restart, ticket, and ABA handling
  into `StreamSessionReconnectService`; the complete validation matrix above passed again.
- Git-tracked first-party runtime, test, native source, contract, script, and CI paths contain
  no Apollo, Sunshine, GameStream, Moonlight, or Vibepollo references.

## Remaining Dynamic Evidence

Task 20 still owns real APK/emulator streamed decode, input, display activation, and final
physical-primary restoration. That work remains paused until the separate ADB/emulator task
releases the shared environment.
