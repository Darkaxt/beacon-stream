# Gate 3 Real-Path Validation Design

**Status:** Approved for implementation

**Parent architecture:** `docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md`

**Parent plan:** `docs/superpowers/plans/2026-07-10-beacon-stream-gates-3-5.md`

## Purpose

Gate 3 must prove that Beacon's one owned streaming path works as a system. Existing tests
prove its components separately, but no test currently crosses all of these boundaries:

```text
Beacon.Server -> named pipe -> Beacon.StreamWorker -> MsQuic
    -> Android production JNI StreamCore
```

The missing proof is product architecture work, not merely a larger test. Worker currently
does not emit deterministic media after `StartSession`, Service does not consume typed input
or feedback events from Worker, and Android instrumentation does not drive production JNI
against a real server session.

## Non-Negotiable Boundaries

- There is one streaming route. The harness must not introduce an alternate transport,
  controller, protocol, Android binding, or test-only media endpoint.
- Service remains control-plane and policy authority.
- Worker remains the only server-side streaming data-plane process.
- Android `BeaconStreamCore` and its production JNI bindings remain the only APK data path.
- Fake host boundaries may replace display, game launch, recovery, and input side effects in
  the acceptance harness. They may not replace Worker, named-pipe IPC, MsQuic, StreamCore,
  ticket authorization, or launch/reconnect grants.
- Test media is a deterministic, non-decodable access-unit marker. Protocol metadata carries
  IDR/end flags, but the bytes are not encoded video and are not decoder-valid H.264. Gate 5
  replaces this source with capture and encoding behind the same Worker contract.
- No startup sleeps, readiness polling, cancellation timeouts, descriptor files, fixed
  streaming ports, or lifecycle watchdogs are allowed.
- Logs and diagnostics may contain identifiers, counts, state, sequence numbers, enums, and
  numeric platform error codes. They must not contain raw tickets, credentials, private
  keys, executable paths, or input payloads.

## Design

### 1. Decouple Host Side Effects From Streaming Mode

`BeaconHostMode` currently selects both host side effects and the streaming implementation.
That prevents a process-level acceptance server from using safe fake display/game/recovery
boundaries with the real Worker route.

Add a separate `BeaconStreamingMode` with values `Fake` and `Worker`.

- Windows host mode defaults to Worker streaming.
- Fake host mode defaults to Fake streaming for existing unit tests.
- An explicit test configuration may select Fake host mode plus Worker streaming.
- The Worker executable path may be explicitly configured for the acceptance process.
- No streaming-mode value is exposed to the APK or public session protocol.

This is dependency composition only. It does not create another runtime implementation.

### 2. Worker Emits One Deterministic Access-Unit Marker

Add a focused `SyntheticMediaSource` invoked by `QuicListener` from an accepted
`StartSession` protocol action.

- It becomes eligible only after ticket authentication and a valid `StartSession`.
- It emits one numbered synthetic access-unit marker with protocol IDR/end flags per
  authenticated session generation.
- It uses the existing 40-byte media datagram header and the real `QuicListener::send` path.
- Chunk size is bounded by the negotiated QUIC datagram maximum.
- It updates encoded-frame, sent-datagram, dropped-frame, and byte metrics through existing
  typed metrics.
- Stop, disconnect, session failure, and Worker shutdown release it exactly once.
- A fresh-ticket reconnect creates an explicit monotonic session generation and emits a new
  access unit. Session id, plan revision, ticket sequence, and native handles are not used as
  generation identifiers.

The payload is deterministic marker data for Gate 3 because Android uses a fake Java
access-unit marker sink. It must not be presented as decodable H.264 or encoding proof.

### 3. Worker IPC Carries Unsolicited Typed Events

Extend `worker_ipc.proto` with request-id-zero events:

- transport authenticated;
- transport disconnected;
- input batch received;
- feedback received;
- media evidence updated.

Input uses the existing public `InputBatch` schema rather than a second input model.
Feedback uses the existing `FeedbackStreamEnvelope` body. Worker events carry session id,
session generation, and source sequence so Service can reject stale evidence.

`QuicSessionProtocol` returns typed authentication, StartSession, input, and feedback actions
instead of requiring a second parse of generic transport packets. MsQuic callbacks publish
those actions without blocking on pipe I/O and without invoking arbitrary code while holding
the listener state mutex.

Worker main owns one outbound MPSC queue and is the only named-pipe writer. A command-reader
thread performs blocking reads, dispatches commands sequentially, and enqueues each complete
response vector as one batch. MsQuic callbacks enqueue unsolicited events through the same
queue. This preserves each command response sequence and prevents frame interleaving without
allowing two writers to coordinate through a fragile mutex.

`NamedPipeChannel` supports one concurrent reader and writer without shared mutable error
state. Terminal reader/writer failure uses `CancelIoEx` to wake the peer operation before
joining the command-reader thread. Event production is signaled by native state changes;
there is no periodic drain loop.

### 4. Managed Worker Event Stream

`StreamWorkerNamedPipeClient` routes `request_id > 0` envelopes only to correlated requests.
It translates allowlisted `request_id == 0` envelopes immediately into a Platform-owned,
protocol-neutral `StreamWorkerEvent` model. Raw Worker protobuf events do not cross into
Server.

`StreamWorkerProcessHost` owns one fixed-capacity event channel for its entire lifetime and
exposes its reader through `IStreamWorkerHost`. Each initialized Worker receives an explicit
monotonic process generation. Worker replacement does not complete the stable channel; old
process/session generations are rejected before relay work.

Core's protocol-neutral input model represents pointer action and wheel delta, keyboard scan
code and state, controller control/value, and touch rational coordinates/pressure. It does
not import Worker protobuf contracts. Platform translation retains exact input values only
until forwarding and gives every input value a redacted diagnostic representation.

`StreamWorkerStreamingBackend` remains authoritative for runtime ownership. It atomically
binds Worker authentication generation to the current Service runtime generation and
validates process generation, session id, Worker session generation, and Service runtime
generation before accepting later events.

A hosted `StreamWorkerEventRelay` performs these mappings:

- Worker input event -> `ClientInputBatch` -> `IClientInputSink.ForwardAsync`;
- Worker feedback event -> sanitized diagnostic/metric journal entry;
- Worker transport/session event -> streaming runtime state update;
- Worker process exit -> runtime invalidation without terminating Beacon.Server.

The relay catches failures per event so one malformed event, sink failure, or Worker exit
cannot terminate the hosted service. It logs only fixed messages and allowlisted metadata
such as event count, event kinds, sequence, and success. It never logs exception messages,
renders raw envelopes, or serializes input payloads into diagnostics.

### 5. Production Android Acceptance Flow

Add Android instrumentation methods that accept `serverUrl` and `clientId` arguments and use
the current `BeaconApiClient`, `BeaconViewModel`, `BeaconStreamSession`, production JNI
bindings, and fake Java frame sink.

The Windows runner starts a real Beacon.Server child process with:

- fake host side effects;
- Worker streaming mode;
- an explicit freshly built Worker executable;
- test-host security and isolated credential/identity/profile storage;
- an OS-assigned HTTP port bound for emulator access.

The runner reads the structured listening event from server stdout. It does not poll a fixed
port. Android reaches the server at `10.0.2.2` and the launch grant derives the QUIC host from
that control-plane route.

Acceptance is split into explicit instrumentation invocations while the same server session
remains alive:

1. register/beacon/capabilities/telemetry/plan/launch;
2. production JNI connects and receives the synthetic access-unit marker;
3. APK sends one input batch and automatic queue feedback;
4. APK closes transport without quitting the server-owned session;
5. reconnect obtains a fresh ticket and receives a new access unit;
6. APK explicitly stops;
7. the runner terminates the exact Worker child process;
8. Beacon.Server remains healthy and reports the runtime invalidated;
9. emergency restore succeeds from structured server state.

Each invocation reports structured instrumentation evidence. HTTP success alone and a
nonblank Surface are not accepted as stream proof.

### 6. Correct FakeEndpoint Lifecycle Semantics

`FakeEndpointRunner` currently asks for reconnect after disconnect, although disconnect has
already made that runtime unavailable. Reorder the live scenario to:

```text
launch -> input -> reconnect -> disconnect -> plan -> quit -> emergency restore
```

The unit handler must model runtime state and return the same 503 behavior as Beacon.Server.
A process-level FakeEndpoint test must run against a real fake-host server.

### 7. Static And Secret-Absence Proof

Expand the architecture scan to tracked source, test, build, script, CI, HTML, CSS,
properties, solution, and props files. The prohibited set includes Apollo, Sunshine,
GameStream, Moonlight, RTSP, RTP, WebRTC, HTTP media transport, external streaming wrappers,
descriptor routes, and parallel Android streaming routes.

The Gate 3 runner captures:

- Beacon.Server stdout/stderr;
- Worker typed diagnostics and process output;
- the server diagnostic journal;
- Android instrumentation output and logcat.

It injects five unique canaries and asserts all captures omit them:

1. raw stream ticket;
2. client credential;
3. private-key export marker;
4. Worker executable path;
5. raw input payload marker.

## Failure Semantics

- A Worker crash fails active streaming state but does not crash Beacon.Server.
- A disconnected transport stops media resources but does not terminate the app/display
  ownership represented by the server session.
- A reconnect requires a newly issued ticket. Ticket replay remains rejected.
- An invalid or stale Worker event is rejected and recorded only as sanitized metadata.
- A failed input injection is surfaced as typed failure evidence without logging the input.
- Emergency restore remains explicit and works after Worker failure.

## Validation Layers

1. Native unit tests prove synthetic packet production and typed event emission.
2. Managed unit tests prove event correlation, generation isolation, input mapping, and
   redaction.
3. Server integration tests prove Fake host plus Worker composition and live FakeEndpoint
   semantics.
4. Android unit/instrumentation tests prove production JNI lifecycle.
5. The Windows/emulator runner proves the complete real path and crash recovery.
6. Full repository commands prove no regression across .NET, native, Android, Client Lab,
   DisplayProbe, GameProbe, and static architecture boundaries.

## Gate 3 Exit Criteria

- The complete Service-to-emulator path passes using one Worker and one StreamCore route.
- One synthetic access-unit marker is observed on first connect and fresh-ticket reconnect.
- APK input reaches `IClientInputSink`; feedback reaches Service diagnostics/metrics.
- Worker termination does not terminate Beacon.Server and emergency restore succeeds.
- FakeEndpoint live flow matches server lifecycle semantics.
- Every prohibited-route scan is empty.
- Every five-canary log scan is empty.
- Full validation passes before sync and again after the evidence-driven refactor audit.
