# StreamWorker Host Contract Ownership Validation

Date: 2026-07-14

## Scope

This Gate 5 ownership slice removes the alternate StreamWorker host lifecycle contract. It does
not run Android device or emulator commands, change display topology, or inspect any installed
third-party streaming product.

## Root Cause

`StreamWorkerStreamingBackend` accepted the weaker `IStreamWorkerHost` contract and silently
wrapped hosts lacking process-generation events in `LegacyGenerationBoundStreamWorkerHost`.
Production composition used the real generation-aware process host, but compatibility tests kept
the synthetic path available. That allowed two different Worker identity and command-lifecycle
semantics inside a boundary intended to have one owner.

## Change

- `IStreamWorkerHost` now owns readiness, identity, process generation, events, generation checks,
  generation-bound commands, and shutdown.
- `IGenerationBoundStreamWorkerHost` and `LegacyGenerationBoundStreamWorkerHost` were removed.
- `StreamWorkerStreamingBackend`, `StreamWorkerEventRelay`, and dependency injection consume the
  one host contract.
- Compatibility-only backend tests and exact-old-interface assertions were removed.
- `StreamWorkerBackendHasOneAuthoritativeHostContract` prevents either split contract from
  returning.

## TDD Evidence

The new architecture test failed first because `IGenerationBoundStreamWorkerHost` was still
declared in `StreamWorkerProcessHost.cs`. It passed after the production and composition paths
were unified.

## Validation

```text
dotnet build Beacon.slnx --no-restore --verbosity minimal
Build succeeded. 0 Warning(s), 0 Error(s).

dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --no-restore --verbosity minimal
Passed: 153, Failed: 0, Skipped: 0.

dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --no-restore --verbosity minimal
Passed: 162, Failed: 0, Skipped: 0.

dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --no-restore --verbosity minimal
Passed: 150, Failed: 0, Skipped: 0.

dotnet test Beacon.slnx --no-restore --verbosity minimal
Passed: 511, Failed: 0, Skipped: 0.

scripts/test-gate3.ps1 -ValidateFixtures
BEACON_GATE3_STATIC_ABSENCE_OK
BEACON_GATE3_SECRET_FIXTURES_OK
BEACON_GATE3_KESTREL_PARSER_OK
BEACON_GATE3_FIXTURES_OK

scripts/test-stream-worker-integration.ps1
BEACON_WORKER_IPC_QUIC_OK AUTH INPUT FEEDBACK REAL_H264_ACCESS_UNIT DISCONNECT SHUTDOWN
BEACON_WORKER_BENCHMARK_OK AUTH RELIABLE DATAGRAM RTT DISCONNECT SHUTDOWN
BEACON_WORKER_STARTUP_EXIT 64
```

## Remaining Ownership Gap

`StreamWorkerSessionAuthorizer` still uses the unpinned `IStreamWorkerHost.SendAsync` overload,
and `Beacon.Core.Streaming` still exposes Worker-named authorization records. The next recovery
slice must make authorization generation-owned, rename the Core contract around generic stream
runtime authority, and remove the unpinned host command path. This evidence does not claim that
work is complete.
