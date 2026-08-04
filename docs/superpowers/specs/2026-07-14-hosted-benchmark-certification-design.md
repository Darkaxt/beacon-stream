# Hosted Benchmark Certification Design

**Status:** Approved for implementation

**Parent architecture:** `docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md`

**Parent plan:** `docs/superpowers/plans/2026-07-10-beacon-stream-gates-3-5.md`

## Purpose

Gate 4 has production implementations for server-owned benchmark planning, ticket issuance,
Android hardware observation, APK coordination, and StreamCore benchmark traffic. The current
hosted Android job does not certify those pieces together: its generic instrumentation run skips
the server-backed Gate 4 methods, while the existing moving-frame acceptance uses a fixed
test-only endpoint without the Beacon Server control plane.

This gate must prove both full manual benchmarking and session-preflight benchmarking across:

```text
Beacon.Server TestHost -> typed Worker IPC -> Beacon hosted benchmark Worker -> MsQuic
    -> Android production StreamCore -> BeaconBenchmarkCoordinator -> Beacon.Server completion
```

The result is acceptance infrastructure for the one Beacon architecture. It is not another
product transport or a portable replacement for the Windows production Worker.

## Non-Negotiable Boundaries

- Beacon Server owns benchmark policy, plan construction, run identity, ticket issuance,
  evidence validation, completion, and runtime teardown.
- The hosted Worker reuses the production `WorkerHost`, `AuthorizedQuicTicketStore`,
  `ServerSessionProtocol`, `BenchmarkSource`, and `QuicListener` implementation.
- The APK uses its sole production `BeaconApiClient`, `BeaconBenchmarkCoordinator`, JNI
  `BeaconStreamCore`, benchmark collector, and MsQuic client.
- Worker control uses the existing length-framed `worker_ipc.proto` contract. No command-line
  session descriptor, JSON sidecar, HTTP data plane, or second control protocol is permitted.
- The hosted Worker supports the benchmark transaction only. It reports video unavailable and
  does not simulate display capture, encoding, game launch, input injection, or recovery.
- TestHost may continue to fake display, launcher, activity, input, and recovery side effects.
  It may not fake the benchmark runtime or ticket authorizer when hosted certification is enabled.
- The hosted process is built and launched only by test infrastructure. It is absent from the
  Server package, Worker package, and Android APKs.
- The test does not query, configure, start, stop, or otherwise interact with any external
  streaming installation.
- The local Android emulator and local ADB server remain untouched. Dynamic proof runs on the
  GitHub-hosted Android emulator.
- Lifecycle coordination is event-driven. No product timeout, startup sleep, readiness polling,
  fixed port, or cancellation watchdog is introduced.
- Raw tickets, ticket hashes, credentials, private keys, run tokens, and IPC envelopes must not
  appear in logs or diagnostic markers.

## Considered Designs

### Reuse the Windows acceptance runner

This would exercise the production Windows Worker but would remain coupled to a self-hosted
Windows machine and a locally owned emulator. It cannot provide repeatable hosted certification
and conflicts with the current ADB ownership boundary.

### Inject certified benchmark evidence into the fake runtime

This proves API validation and storage but does not prove the APK's network benchmark, ticket
authorization, QUIC path, or Worker lifecycle. It remains useful for narrow unit tests but is not
Gate 4 acceptance evidence.

### Beacon-native hosted benchmark Worker

This is the selected design. A Linux test executable composes the portable production Worker
primitives and communicates with TestHost through the existing typed IPC. It proves the real
benchmark data path while excluding Windows-only capture and display side effects that are not
part of a network/hardware benchmark.

## Design

### 1. Separate Portable Worker Primitives From Windows Media Primitives

Split the native Worker build into two ownership groups:

- `BeaconStreamWorkerPortableCore`: benchmark generation, ticket authorization, Worker command
  state, Worker events, outbound queue, server session protocol integration, and QUIC listener;
- `BeaconStreamWorkerCore`: the portable core plus Windows named pipes, Windows Graphics Capture,
  D3D11 conversion, NVENC, and production video generation.

Move the small `ProductionVideoCapabilities` value type to a platform-neutral Worker header so
`WorkerHost` does not include WGC or NVENC declarations merely to report capabilities. The Windows
probe remains in the Windows-only production capability implementation.

The Windows executable and all existing native tests continue to link `BeaconStreamWorkerCore`.
Only the hosted benchmark executable links `BeaconStreamWorkerPortableCore`.

### 2. Make QUIC Identity Loading Platform-Correct

`QuicListener` keeps one PKCS#12 identity-path contract. On Windows, it retains the existing
Schannel certificate-context import and imported-key cleanup. On OpenSSL/quictls hosts, it reads
the same bounded PKCS#12 bytes and supplies `QUIC_CREDENTIAL_TYPE_CERTIFICATE_PKCS12` to MsQuic.

The identity bytes remain owned until configuration teardown and are securely cleared on every
success or failure path. Platform-specific certificate handles and error extraction remain behind
compile-time branches. Server and hosted Worker receive the exact same identity file, so the
public-key fingerprint pinned by StreamCore necessarily describes the QUIC endpoint.

The top-level native build selects Schannel on Windows and quictls on non-Windows hosts. Android
continues to use quictls unchanged.

### 3. Add A Test-Only Hosted Benchmark Worker

`BeaconHostedBenchmarkWorker` is a Linux test executable with these arguments only:

```text
--identity <Beacon server PKCS#12 path>
```

It creates a random Worker instance id, constructs the production ticket store, QUIC listener,
WorkerHost, and a no-video pipeline, then exchanges length-prefixed Worker envelopes over binary
stdin/stdout. Stderr carries only fixed failure markers and numeric error categories.

The process publishes the normal hello/capabilities/ready sequence. TestHost then drives the
unchanged command transaction:

```text
PrepareBenchmark -> StartMedia -> AuthorizeTicket
    -> authenticated StartBenchmark/traffic -> StopMedia -> RevokeTicket
```

The no-video pipeline rejects video preparation and performs no side effect. Benchmark preparation
already resets the video boundary and therefore remains fully supported by `WorkerHost`.

### 4. Let TestHost Own The Hosted Worker

Add a test-only `HostedBenchmarkWorkerProcessHost` implementing the existing `IStreamWorkerHost`.
It starts one child with redirected binary stdin/stdout, combines those streams into a duplex
control stream, and delegates framing, handshake validation, request correlation, and unsolicited
event translation to the existing managed Worker client.

The process host:

- starts lazily when `IBenchmarkRuntime.StartAsync` first requires Worker readiness;
- records one explicit process generation and validates it on every command;
- observes child exit through `Process.WaitForExitAsync`;
- sends `ShutdownWorker` during host disposal and owns forced child termination only when the
  control channel has already failed;
- drains fixed stderr diagnostics without echoing arguments or protocol data;
- publishes Worker events through the existing bounded event channel.

TestHost selects this composition only when `Beacon:HostedBenchmarkWorker:Path` is configured.
In that mode it replaces `FakeBenchmarkRuntime` and `FakeStreamSessionAuthorizer` with:

- `StreamWorkerStreamingBackend` as `IBenchmarkRuntime`;
- `StreamWorkerSessionAuthorizer` as `IStreamSessionAuthorizer`;
- the hosted process host as `IStreamWorkerHost`.

The fake streaming backend remains responsible for non-benchmark test sessions. This does not add
a product route; it isolates the benchmark runtime while preserving safe fake host side effects.

### 5. Hosted Emulator Acceptance Runner

Add a Linux runner that:

1. bootstraps pinned native dependencies and host `protoc`;
2. builds `BeaconHostedBenchmarkWorker` with MsQuic/quictls;
3. builds the TestHost and Android instrumentation APK;
4. creates isolated TestHost state and starts TestHost with the hosted Worker path;
5. reads the structured Kestrel listening event rather than polling a port;
6. registers the emulator client through the existing test-host credential mechanism;
7. invokes `gate4NetworkAndHardwareBenchmark`, `gate4CertifiedBenchmarkEvidence`, and
   `gate4CertifiedSessionPreflight` explicitly with `serverUrl` and `clientId`;
8. queries TestHost snapshots and verifies completed manual/preflight evidence, no pending runs,
   Worker readiness, ticket authorization/revocation, and benchmark transport diagnostics;
9. stops TestHost and confirms Worker exit and artifact cleanup.

The emulator connects to Kestrel and the host QUIC listener through `10.0.2.2`. Both listeners use
OS-assigned ports. The runner waits on process output, instrumentation completion, and process exit;
it does not use sleep-based readiness or polling loops.

### 6. Evidence And Failure Semantics

Acceptance requires all of the following:

- the full benchmark invokes the APK's native network path and observes real emulator hardware;
- deterministic certified hardware evidence produces one accepted manual benchmark record;
- session preflight sends network and power evidence without decoder rounds;
- each benchmark grant is authenticated by the hosted Worker using the Server-issued ticket;
- reliable and datagram benchmark samples cross production StreamCore and JNI;
- benchmark completion reaches Beacon Server and removes the pending run;
- explicit stop revokes the ticket and closes the Worker listener;
- TestHost remains healthy after both transactions;
- the hosted process and test-only symbols are absent from shipped artifacts;
- the protected compatibility scan remains empty.

Expected emulator limitations, such as the absence of a sustainable hardware decoder, may produce
the already-defined typed benchmark rejection for the real-hardware observation method. Transport,
authentication, lifecycle, and evidence collection must still succeed; a transport failure is not
an acceptable hardware limitation.

## Validation Layers

1. Native unit tests prove platform-neutral capability types, PKCS#12 credential setup shape,
   stdin/stdout frame decoding, Worker command dispatch, and deterministic shutdown.
2. Managed unit tests prove hosted options validation, duplex stream behavior, process generation,
   Worker handshake, command correlation, exit observation, and service composition.
3. Existing Windows native and managed suites prove the portable-core split does not regress the
   production Worker.
4. Script simulation proves runner parsing, markers, secret absence, and cleanup without ADB.
5. GitHub-hosted Android execution proves the real Server-to-Worker-to-StreamCore benchmark path.
6. A post-pass refactor audit reruns all static, managed, native Windows, Android, and hosted checks.

## Exit Criteria

- Both manual and session-preflight benchmark workflows complete through the hosted Beacon-native
  Worker and production APK StreamCore.
- The real emulator hardware observation method crosses the native network path and returns either
  accepted evidence or only its documented capability rejection.
- TestHost stores accepted certified evidence and has no pending benchmark after each transaction.
- Ticket authorization, revocation, Worker shutdown, and child-process cleanup are directly proven.
- Windows production Worker behavior and existing moving-frame hosted acceptance remain green.
- No forbidden compatibility path, external runtime dependency, descriptor file, timeout, fixed
  port, local ADB action, or shipped test executable is introduced.

