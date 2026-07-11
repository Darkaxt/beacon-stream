# Gate 3 Real-Path Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove one real Beacon.Server to StreamWorker to MsQuic to Android production-JNI path, including media, input, feedback, reconnect, Worker crash isolation, recovery, static boundaries, and secret absence.

**Architecture:** Keep fake display/game/recovery boundaries in the acceptance host while selecting the real Worker streaming boundary independently. Worker emits one deterministic synthetic access unit through its production QUIC listener and publishes typed request-id-zero events over its existing named pipe; Service relays those events to input and diagnostics, while Android instrumentation uses the production StreamCore JNI route.

**Tech Stack:** .NET 10, ASP.NET Core, C# channels and hosted services, C++20, Protobuf, Windows named pipes, MsQuic, Java/JNI, Android instrumentation, PowerShell, CMake/CTest, Gradle.

**Design:** `docs/superpowers/specs/2026-07-11-gate3-real-path-validation-design.md`

---

### Task 1: Compose Fake Host Side Effects With Real Worker Streaming

**Files:**
- Create: `src/Beacon.Server/Hosting/BeaconStreamingMode.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `src/Beacon.Platform.Windows/Streaming/StreamWorkerProcessHost.cs`
- Test: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`
- Test: `tests/Beacon.Platform.Windows.Tests/Streaming/StreamWorkerProcessHostTests.cs`

- [x] **Step 1: Write failing composition and path-resolution tests**

Add cases proving Fake/Fake remains the default, Windows/Worker remains the production
default, Fake/Worker is explicitly selectable, and an explicit Worker path wins over the
default:

```csharp
[Fact]
public void FakeHostCanUseWorkerStreamingWithoutWindowsSideEffects()
{
    using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
    {
        [BeaconServiceRegistration.HostModeConfigurationKey] = "fake",
        [BeaconServiceRegistration.StreamingModeConfigurationKey] = "worker",
        [BeaconServiceRegistration.StreamWorkerPathConfigurationKey] = workerPath
    });

    Assert.IsType<FakeDisplayBackend>(provider.GetRequiredService<IDisplayBackend>());
    Assert.IsType<StreamWorkerStreamingBackend>(provider.GetRequiredService<IStreamingBackend>());
    Assert.Equal(workerPath,
        provider.GetRequiredService<StreamWorkerProcessHostOptions>().ExecutablePath);
}
```

- [x] **Step 2: Run the focused tests and verify the expected failure**

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter BeaconServiceRegistrationTests
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter StreamWorkerProcessHostTests
```

Expected: failure because streaming mode and explicit Worker path do not exist.

- [x] **Step 3: Add the minimal composition model**

Use this closed enum and configuration surface:

```csharp
public enum BeaconStreamingMode { Fake, Worker }

public const string StreamingModeConfigurationKey = "Beacon:StreamingMode";
public const string StreamingModeEnvironmentVariable = "BEACON_STREAMING_MODE";
public const string StreamWorkerPathConfigurationKey = "Beacon:Streaming:WorkerPath";
public const string StreamWorkerPathEnvironmentVariable = "BEACON_STREAM_WORKER_PATH";
```

Resolve defaults as Fake for Fake host mode and Worker for Windows host mode. Keep
`AddHostBoundaries` selected only by host mode and `AddStreamingBoundary` selected only by
streaming mode. Reject unknown values with the accepted values in the exception.

- [x] **Step 4: Run focused tests, full registration tests, and format**

```powershell
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter BeaconServiceRegistrationTests
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter StreamWorkerProcessHostTests
dotnet format Beacon.slnx --verify-no-changes
```

Expected: all pass and format reports no changes.

- [x] **Step 5: Commit and push the slice**

```powershell
git add src/Beacon.Server/Hosting src/Beacon.Platform.Windows/Streaming/StreamWorkerProcessHost.cs tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs tests/Beacon.Platform.Windows.Tests/Streaming/StreamWorkerProcessHostTests.cs
git commit -m "test: compose real Worker with fake host boundaries"
git push -u origin codex/beacon-gate3-validation
```

### Task 2: Emit Deterministic Media And Typed Worker Events

**Files:**
- Modify: `contracts/worker_ipc.proto`
- Create: `src/Beacon.StreamWorker/include/beacon/worker/synthetic_media_source.h`
- Create: `src/Beacon.StreamWorker/src/synthetic_media_source.cpp`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/worker_host.h`
- Modify: `src/Beacon.StreamWorker/src/worker_host.cpp`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/quic_listener.h`
- Modify: `src/Beacon.StreamWorker/src/quic_listener.cpp`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/quic_session_protocol.h`
- Modify: `src/Beacon.StreamWorker/src/quic_session_protocol.cpp`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/named_pipe_channel.h`
- Modify: `src/Beacon.StreamWorker/src/named_pipe_channel.cpp`
- Modify: `src/Beacon.StreamWorker/src/main.cpp`
- Modify: `src/Beacon.StreamWorker/CMakeLists.txt`
- Test: `tests/Beacon.StreamWorker.Tests/worker_host_tests.cpp`
- Test: `tests/Beacon.StreamWorker.Tests/quic_session_tests.cpp`

- [ ] **Step 1: Write failing native tests**

Add tests that authenticate/start a session and assert one valid numbered IDR datagram,
then assert input, feedback, disconnect, and media evidence become request-id-zero IPC events.
The central expectations are:

```cpp
BEACON_TEST_REQUIRE(events.size() >= 3);
BEACON_TEST_REQUIRE(events[0].request_id() == 0);
BEACON_TEST_REQUIRE(events[0].body_case() == WorkerIpcEnvelope::kTransportAuthenticated);
BEACON_TEST_REQUIRE(
    events[1].input_received().input().input_batch().events_size() == 1);
BEACON_TEST_REQUIRE(events[2].feedback_received().feedback().sequence() == 1);

const auto datagrams = transport.take_sent_packets();
BEACON_TEST_REQUIRE(datagrams.size() == 1);
BEACON_TEST_REQUIRE(datagrams.front().channel == StreamChannel::media);
BEACON_TEST_REQUIRE(parse_media_header(datagrams.front()).sequence == 1);
```

- [ ] **Step 2: Run native tests and verify failure**

```powershell
.\scripts\build-native-windows.ps1
```

Expected: compilation/test failure because the event messages and synthetic source are absent.

- [ ] **Step 3: Extend the IPC schema with typed events**

Import `stream_control.proto` and add these oneof bodies and messages without duplicating the
public input/feedback schema. The event also carries an explicit monotonic generation created
at successful authentication; do not derive it from session id, plan revision, ticket state,
packet sequence, or an `HQUIC` value:

```proto
TransportAuthenticated transport_authenticated = 34;
TransportDisconnected transport_disconnected = 35;
InputReceived input_received = 36;
FeedbackReceived feedback_received = 37;
MediaEvidence media_evidence = 38;

message InputReceived {
  uint64 session_generation = 1;
  beacon.stream.v1.InputStreamEnvelope input = 2;
}
message FeedbackReceived {
  uint64 session_generation = 1;
  beacon.stream.v1.FeedbackStreamEnvelope feedback = 2;
}
```

Every unsolicited event must set `request_id = 0`, the owning `session_id`, and its session
generation. Extend `QuicSessionProtocolOutput` with typed accepted-authentication,
accepted-StartSession, input, and feedback actions so QuicListener does not reparse generic
packet payloads. Regenerate C# and C++ contracts through the existing build.

- [ ] **Step 4: Implement the source and serialized event writer**

Implement `SyntheticMediaSource::emit_idr` as a pure packet builder whose output enters
`QuicListener::send` after the accepted StartSession action. Add one outbound MPSC queue;
`main.cpp` is its sole named-pipe consumer/writer, while one command-reader thread performs
blocking reads and enqueues complete response vectors as indivisible batches. QuicListener
publishes typed events through the same queue without holding its state mutex or waiting on
pipe backpressure. Do not add periodic loops or sleep.

```cpp
class SyntheticMediaSource final {
 public:
  std::vector<stream::TransportPacket> emit_idr(
      std::uint64_t sequence,
      std::uint64_t presentation_time_us,
      std::uint16_t maximum_datagram_bytes) const;
};
```

Make `NamedPipeChannel` safe for one concurrent reader and writer by removing shared mutable
error state. Add an explicit `cancel_pending_io()` using `CancelIoEx` so terminal queue,
reader, or writer failure wakes the peer operation before the command-reader thread is joined.
Never close the pipe handle while either operation can still use it.

- [ ] **Step 5: Run native and contract validation**

```powershell
.\scripts\build-native-windows.ps1
dotnet test tests\Beacon.StreamWorker.Contracts.Tests\Beacon.StreamWorker.Contracts.Tests.csproj
.\scripts\test-stream-worker-integration.ps1
.\scripts\test-stream-worker-quic-loopback.ps1
```

Expected: all pass, one media packet is observed, and no modal assertion process remains.

- [ ] **Step 6: Commit and push**

```powershell
git add contracts/worker_ipc.proto src/Beacon.StreamWorker tests/Beacon.StreamWorker.Tests
git commit -m "feat: publish Worker media and stream events"
git push
```

### Task 3: Relay Worker Input And Feedback Through Service

**Files:**
- Modify: `src/Beacon.Platform.Windows/Streaming/StreamWorkerNamedPipeClient.cs`
- Modify: `src/Beacon.Platform.Windows/Streaming/StreamWorkerProcessHost.cs`
- Create: `src/Beacon.Platform.Windows/Streaming/StreamWorkerEvent.cs`
- Create: `src/Beacon.Server/Streaming/StreamWorkerEventRelay.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Test: `tests/Beacon.Platform.Windows.Tests/Streaming/StreamWorkerNamedPipeClientTests.cs`
- Test: `tests/Beacon.Platform.Windows.Tests/Streaming/StreamWorkerProcessHostTests.cs`
- Create: `tests/Beacon.Server.Tests/StreamWorkerEventRelayTests.cs`

- [ ] **Step 1: Write failing request-correlation and relay tests**

Prove request-id-zero events never complete a correlated command, stale Worker generations
are discarded, input is mapped exactly once, and diagnostics contain only metadata:

```csharp
WorkerIpcEnvelope envelope = await host.Events.ReadAsync(cancellationToken);
Assert.Equal(0UL, envelope.RequestId);

await relay.HandleAsync(envelope, cancellationToken);
ClientInputBatch batch = Assert.Single(inputSink.Batches);
Assert.Equal("z-fold-7", batch.ClientId);
Assert.Single(batch.Events);
Assert.DoesNotContain(rawInputCanary, journal.RenderForTest());
```

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter "StreamWorkerNamedPipeClientTests|StreamWorkerProcessHostTests"
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter StreamWorkerEventRelayTests
```

Expected: failure because `IStreamWorkerHost` has no event reader and request-id-zero frames
are ignored.

- [ ] **Step 3: Implement a bounded event channel and stable host surface**

Expose events without making Core depend on Worker contracts:

```csharp
public interface IStreamWorkerHost
{
    ChannelReader<WorkerIpcEnvelope> Events { get; }
    // Existing members remain unchanged.
}
```

`StreamWorkerNamedPipeClient.ReceiveLoopAsync` writes request-id-zero frames to its event
channel. `StreamWorkerProcessHost` forwards only the current process generation and completes
the old generation before replacement.

- [ ] **Step 4: Implement the hosted relay**

Register one `BackgroundService` that awaits `Events.ReadAllAsync(stoppingToken)`. Map public
input schema to `ClientInputBatch`, call `IClientInputSink`, and publish diagnostic metadata
for feedback, transport, media, and failures. Never call `ToString()` on the incoming envelope
or event payload.

- [ ] **Step 5: Run focused and full managed tests**

```powershell
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj
dotnet test Beacon.slnx --no-build
```

Expected: all pass with no raw input canary in captured diagnostics.

- [ ] **Step 6: Commit and push**

```powershell
git add src/Beacon.Platform.Windows/Streaming src/Beacon.Server/Streaming src/Beacon.Server/Hosting tests/Beacon.Platform.Windows.Tests tests/Beacon.Server.Tests
git commit -m "feat: relay Worker events through Beacon Service"
git push
```

### Task 4: Drive Production JNI Against The Real Service And Worker

**Files:**
- Modify: `src/Beacon.Android/app/src/androidTest/java/dev/beacon/android/BeaconStreamCoreInstrumentationTest.java`
- Create: `src/Beacon.Android/app/src/androidTest/java/dev/beacon/android/Gate3SessionEvidence.java`
- Create: `scripts/test-gate3-emulator-session.ps1`
- Modify: `scripts/test-android.ps1`
- Test: `tests/Beacon.Server.Tests/ClientApiTests.cs`

- [ ] **Step 1: Add failing instrumentation contract tests**

Add two separately invokable methods, `gate3ConnectSendAndDisconnect` and
`gate3ReconnectAndStop`, that require `serverUrl` and `clientId` instrumentation arguments.
They must use production `BeaconStreamCore` bindings and report:

```java
assertTrue(evidence.transportConnected());
assertEquals(1L, evidence.receivedFrameCount());
assertTrue(evidence.inputSent());
assertTrue(evidence.feedbackSent());
assertTrue(evidence.transportClosed());
```

The second method asserts a different ticket fingerprint and a new received-frame sequence.

- [ ] **Step 2: Run Android build and verify the new acceptance test fails without a host**

```powershell
gradle -p src\Beacon.Android assembleDebug assembleDebugAndroidTest --console=plain
```

Expected: compilation passes; invoking either Gate 3 method without arguments fails closed
with the missing argument name.

- [ ] **Step 3: Implement the process-level PowerShell runner**

The script must:

1. build Server, Worker, APK, and test APK;
2. create isolated temporary identity/credential/profile paths;
3. start Beacon.Server with Fake host and Worker streaming;
4. read the structured Kestrel listening line and derive the emulator URL;
5. install APKs and invoke each exact instrumentation method;
6. identify the Worker by parent process id and terminate only that child;
7. verify Server health, runtime invalidation, and emergency restore;
8. clean up only processes and temporary paths it created.

Use process exit handles, stdout events, and exact child-process identity. Do not use sleeps,
fixed ports, or repeated HTTP readiness probes.

- [ ] **Step 4: Run the real emulator session**

```powershell
.\scripts\test-gate3-emulator-session.ps1 -Serial emulator-5554
```

Expected terminal evidence:

```text
BEACON_GATE3_READY
BEACON_GATE3_FRAME 1
BEACON_GATE3_INPUT_ECHO 1
BEACON_GATE3_FEEDBACK 1
BEACON_GATE3_RECONNECT_FRESH_TICKET
BEACON_GATE3_WORKER_CRASH_ISOLATED
BEACON_GATE3_EMERGENCY_RESTORE_OK
```

- [ ] **Step 5: Commit and push**

```powershell
git add src/Beacon.Android/app/src/androidTest scripts/test-gate3-emulator-session.ps1 scripts/test-android.ps1 tests/Beacon.Server.Tests/ClientApiTests.cs
git commit -m "test: prove Beacon real path on Android emulator"
git push
```

### Task 5: Correct FakeEndpoint And Complete Gate 3 Guards

**Files:**
- Modify: `src/Beacon.FakeEndpoint/FakeEndpointRunner.cs`
- Modify: `tests/Beacon.FakeEndpoint.Tests/FakeEndpointRunnerTests.cs`
- Create: `tests/Beacon.Server.Tests/FakeEndpointLiveTests.cs`
- Modify: `tests/Beacon.Core.Tests/Architecture/ArchitectureRecoveryBoundaryTests.cs`
- Create: `scripts/test-gate3-secret-absence.ps1`
- Create: `scripts/test-gate3.ps1`
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`

- [ ] **Step 1: Write failing lifecycle, scan, and canary tests**

Change the expected FakeEndpoint order to:

```text
POST launch
POST input
POST reconnect
POST disconnect
POST plan
POST quit
POST emergency-restore
```

Make the test handler track whether a runtime is active and return 503 for reconnect after
disconnect. Add architecture cases covering `.github`, root build files, `.sh`, `.html`,
`.css`, `.properties`, `.slnx`, and `.props`, including WebRTC and HTTP-media aliases.

- [ ] **Step 2: Run focused tests and verify failures**

```powershell
dotnet test tests\Beacon.FakeEndpoint.Tests\Beacon.FakeEndpoint.Tests.csproj
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter ArchitectureRecoveryBoundaryTests
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter FakeEndpointLiveTests
```

Expected: old ordering and incomplete file/token coverage fail.

- [ ] **Step 3: Implement lifecycle correction and tracked-file guard**

Reorder only the scenario; do not weaken Server lifecycle rules. Enumerate tracked files with
`git ls-files -z` in the PowerShell guard and apply the same prohibited set used by the C#
test. Exclude only audited generated dependency/build directories, never tracked runtime or
test code.

- [ ] **Step 4: Implement five-canary capture**

Capture server stdout/stderr, Worker diagnostics, diagnostic journal JSON, instrumentation
output, and logcat. Assert none contains the exact ticket, credential, private-key marker,
Worker executable path, or input-payload marker generated for that run. Print only the canary
category on failure, not its value.

- [ ] **Step 5: Run every Gate 3 command through one runner**

```powershell
.\scripts\test-gate3.ps1 -Serial emulator-5554
```

The runner executes .NET restore/format/build/test, Windows native build/CTest/integration,
Android clean/unit/build/native/instrumentation, Client Lab lint/test/Playwright,
FakeEndpoint live flow, DisplayProbe status, GameProbe scan, real emulator session, static
absence, and secret absence. It returns nonzero at the first failed command and prints one
structured summary after all required stages pass.

- [ ] **Step 6: Commit and push**

```powershell
git add src/Beacon.FakeEndpoint tests/Beacon.FakeEndpoint.Tests tests/Beacon.Server.Tests tests/Beacon.Core.Tests/Architecture scripts .github/workflows/ci.yml README.md
git commit -m "test: complete Gate 3 validation matrix"
git push
```

### Task 6: Validate, Sync, Refactor, Revalidate, Sync

**Files:**
- Modify only files justified by a failing regression during the audit.
- Update: `docs/superpowers/plans/2026-07-10-beacon-stream-gates-3-5.md`
- Update: `docs/superpowers/plans/2026-07-11-gate3-real-path-validation.md`
- Update: `docs/extraction-map.md`

- [ ] **Step 1: Run static and dynamic validation**

```powershell
.\scripts\test-gate3.ps1 -Serial emulator-5554
git diff --check
git status --short
```

Expected: every stage passes, diff check is clean, and only intended files are modified.

- [ ] **Step 2: Open and merge the implementation PR**

```powershell
gh pr create --fill --base main --head codex/beacon-gate3-validation
gh pr checks --watch
gh pr merge --squash --delete-branch
git switch main
git pull --ff-only
```

Wait for every duplicate CI job. Do not merge with a queued, skipped, canceled, or failed
required job.

- [ ] **Step 3: Create the refactor branch and audit from evidence**

```powershell
git switch -c codex/beacon-gate3-refactor
```

Audit only duplication, ownership leaks, generated drift, JNI resource imbalance, unsafe
buffer lifetime, compatibility residue, and secret exposure. Before each change, add a test
that fails against synchronized `main`. Do not add product features.

- [ ] **Step 4: Re-run the complete matrix**

```powershell
.\scripts\test-gate3.ps1 -Serial emulator-5554
git diff --check
```

Expected: the same Gate 3 evidence passes after refactoring.

- [ ] **Step 5: Commit, push, merge, and synchronize again**

```powershell
git add -A
git commit -m "refactor: harden Beacon Gate 3 ownership"
git push -u origin codex/beacon-gate3-refactor
gh pr create --fill --base main --head codex/beacon-gate3-refactor
gh pr checks --watch
gh pr merge --squash --delete-branch
git switch main
git pull --ff-only
git status --short --branch
```

Expected: clean synchronized `main`, Gate 3 tasks checked complete, and Gate 4 remains the
next unstarted product work.
