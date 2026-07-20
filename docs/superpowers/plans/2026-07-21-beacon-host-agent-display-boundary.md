# Beacon Host Agent Display Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run Beacon's Windows display primitives through one elevated, interactive, ACL-restricted Host Agent so the non-elevated Beacon Service can create, preserve, activate, restore, and remove per-client SudoVDA desktops without recurring UAC prompts.

**Architecture:** A managed Host Agent owns the existing `WindowsDisplayApi` and its SudoVDA heartbeat session. Beacon Service uses one persistent named-pipe connection through a proxy implementing `IWindowsDisplayApi`, `IWindowsDisplayLeaseSession`, and `IWindowsDisplayNameResolver`; policy remains in `WindowsDisplayBackend` and `DisplayLeaseManager`. This plan deliberately excludes driver package installation, which receives a second plan after this pipe and display boundary pass dynamic validation.

**Tech Stack:** .NET 10, C#, Windows named pipes and ACLs, System.Text.Json strict envelopes, existing Beacon display abstractions, xUnit, Task Scheduler installation script.

---

## File Map

- `src/Beacon.HostAgent.Contracts`: Beacon-owned local protocol, strict DTOs, and bounded framing shared by the agent and Windows client adapter.
- `src/Beacon.HostAgent`: no-console elevated executable, pipe ACL/caller validation, request dispatcher, and the sole production owner of direct `WindowsDisplayApi`.
- `src/Beacon.Platform.Windows/HostAgent`: persistent client connection and the proxy implementing existing display interfaces.
- `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`: production dependency injection selects the Host Agent proxy; direct APIs remain injectable only in tests and Host Agent.
- `tests/Beacon.HostAgent.Contracts.Tests`: strict protocol/framing tests.
- `tests/Beacon.HostAgent.Tests`: ACL, caller, dispatcher, and disconnect behavior using fake display mechanics.
- `tests/Beacon.Platform.Windows.Tests/HostAgent`: persistent client and display-proxy behavior.
- `scripts/install-beacon-host-agent.ps1`: explicit one-time elevated task registration whose action is the no-console executable.
- `scripts/uninstall-beacon-host-agent.ps1`: deterministic task removal without deleting user state or driver packages.

### Task 1: Add The Strict Local Contract

**Files:**
- Create: `src/Beacon.HostAgent.Contracts/Beacon.HostAgent.Contracts.csproj`
- Create: `src/Beacon.HostAgent.Contracts/HostAgentProtocol.cs`
- Create: `src/Beacon.HostAgent.Contracts/HostAgentFrameCodec.cs`
- Create: `tests/Beacon.HostAgent.Contracts.Tests/Beacon.HostAgent.Contracts.Tests.csproj`
- Create: `tests/Beacon.HostAgent.Contracts.Tests/HostAgentProtocolTests.cs`
- Modify: `Beacon.slnx`

- [ ] **Step 1: Write failing contract tests**

Cover exact round-trip behavior, unknown top-level member rejection, unknown operation rejection, negative/oversized frame rejection, truncated frame rejection, and a maximum 64 KiB payload. Use these public shapes:

```csharp
public enum HostAgentOperation
{
    GetStatus,
    HoldDisplayLease,
    ReleaseDisplayLease,
    CreateVirtualDisplay,
    QueryTopology,
    SetVirtualPrimary,
    RestorePhysicalPrimary,
    RemoveVirtualDisplay,
    QueryHdrCapability
}

public sealed record HostAgentRequest(
    int ProtocolVersion,
    Guid RequestId,
    HostAgentOperation Operation,
    JsonElement Payload);

public sealed record HostAgentResponse(
    int ProtocolVersion,
    Guid RequestId,
    bool Success,
    string ResultCode,
    string Diagnostic,
    JsonElement Payload);
```

- [ ] **Step 2: Run the contract tests and verify RED**

Run:

```powershell
dotnet test tests\Beacon.HostAgent.Contracts.Tests\Beacon.HostAgent.Contracts.Tests.csproj
```

Expected: compile failure because the contract project and types do not exist.

- [ ] **Step 3: Implement strict serialization and framing**

`HostAgentProtocol` must expose `CurrentVersion = 1`, `MaximumFrameBytes = 65_536`, web JSON naming, string enums, and `JsonUnmappedMemberHandling.Disallow`. `HostAgentFrameCodec` writes a four-byte little-endian length followed by one JSON envelope and reads exactly that shape. It accepts caller cancellation but creates no internal timeout.

- [ ] **Step 4: Run the contract tests and verify GREEN**

Expected: all `HostAgentProtocolTests` pass.

- [ ] **Step 5: Commit the contract slice**

```powershell
git add Beacon.slnx src/Beacon.HostAgent.Contracts tests/Beacon.HostAgent.Contracts.Tests
git commit -m "feat: define Beacon Host Agent protocol"
```

### Task 2: Build The Agent Pipe And Caller Boundary

**Files:**
- Create: `src/Beacon.HostAgent/Beacon.HostAgent.csproj`
- Create: `src/Beacon.HostAgent/HostAgentPipeIdentity.cs`
- Create: `src/Beacon.HostAgent/HostAgentPipeServer.cs`
- Create: `src/Beacon.HostAgent/IHostAgentCallerVerifier.cs`
- Create: `src/Beacon.HostAgent/WindowsHostAgentCallerVerifier.cs`
- Create: `tests/Beacon.HostAgent.Tests/Beacon.HostAgent.Tests.csproj`
- Create: `tests/Beacon.HostAgent.Tests/HostAgentPipeIdentityTests.cs`
- Create: `tests/Beacon.HostAgent.Tests/HostAgentPipeServerTests.cs`
- Modify: `Beacon.slnx`

- [ ] **Step 1: Write failing pipe-boundary tests**

Assert that the pipe name is deterministic from the owning SID and contains no account name; its protected ACL grants full control only to the owner SID, LocalSystem, and Builtin Administrators. Assert that a rejected caller receives no dispatched request and that disconnect closes only the connection.

```csharp
SecurityIdentifier owner = new("S-1-5-21-100-200-300-1001");
Assert.Equal(
    "beacon-host-agent-S-1-5-21-100-200-300-1001-v1",
    HostAgentPipeIdentity.CreateName(owner));
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test tests\Beacon.HostAgent.Tests\Beacon.HostAgent.Tests.csproj --filter "HostAgentPipe"
```

Expected: compile failure because the Agent project is absent.

- [ ] **Step 3: Implement the fixed local pipe boundary**

Create the pipe with `NamedPipeServerStreamAcl.Create`, byte mode, asynchronous I/O, a protected ACL, and no inheritable handle. `WindowsHostAgentCallerVerifier` must use kernel pipe-client identity, require the configured owner SID, reject remote clients, and never trust a SID or PID supplied inside JSON. The accept loop creates the next pipe instance before serving the accepted connection and observes every handler task.

- [ ] **Step 4: Run tests and verify GREEN**

Expected: ACL, caller rejection, accepted caller, and clean disconnect tests pass.

- [ ] **Step 5: Commit the pipe slice**

```powershell
git add Beacon.slnx src/Beacon.HostAgent tests/Beacon.HostAgent.Tests
git commit -m "feat: add restricted Host Agent pipe"
```

### Task 3: Dispatch Existing Display Mechanics Without Moving Policy

**Files:**
- Create: `src/Beacon.HostAgent/IHostAgentDisplayExecutor.cs`
- Create: `src/Beacon.HostAgent/WindowsHostAgentDisplayExecutor.cs`
- Create: `src/Beacon.HostAgent/HostAgentDispatcher.cs`
- Create: `src/Beacon.HostAgent.Contracts/HostAgentDisplayPayloads.cs`
- Create: `tests/Beacon.HostAgent.Tests/HostAgentDispatcherTests.cs`

- [ ] **Step 1: Write failing dispatcher tests**

Use a fake executor and assert exact routing for status, hold/release, create, query, primary, restore, remove, and HDR. Also assert malformed payload rejection, protocol mismatch, result-code preservation, and that connection disposal never calls release/remove/restore.

```csharp
public interface IHostAgentDisplayExecutor
{
    DisplayDriverStatus GetDriverStatus();
    SudoVdaDriverLeaseSessionSnapshot GetLeaseSnapshot();
    Task<SudoVdaDriverLeaseHoldResult> HoldAsync(string displayId, CancellationToken cancellationToken);
    Task ReleaseAsync(string displayId, CancellationToken cancellationToken);
    Task<DisplayApiResult> CreateAsync(CreateVirtualDisplayPayload request, CancellationToken cancellationToken);
    Task<DisplayTopologySnapshot> QueryAsync(CancellationToken cancellationToken);
    Task<DisplayApiResult> SetVirtualPrimaryAsync(string displayId, CancellationToken cancellationToken);
    Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken);
    Task<DisplayApiResult> RemoveAsync(string displayId, CancellationToken cancellationToken);
    Task<DisplayHdrCapability> QueryHdrAsync(string displayId, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Run dispatcher tests and verify RED**

Expected: compile failure for the missing executor and dispatcher.

- [ ] **Step 3: Implement the dispatcher and production executor**

The production executor wraps one singleton `WindowsDisplayApi` and its `IWindowsDisplayLeaseSession`. The dispatcher deserializes exactly one payload type selected by the operation enum and translates only existing Beacon display records. It catches expected validation/platform failures into stable result codes and lets process-fatal failures terminate the Agent rather than returning fabricated success.

- [ ] **Step 4: Run dispatcher tests and existing display tests**

```powershell
dotnet test tests\Beacon.HostAgent.Tests\Beacon.HostAgent.Tests.csproj --filter HostAgentDispatcher
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter FullyQualifiedName~Displays
```

Expected: dispatcher tests pass and existing display tests remain green.

- [ ] **Step 5: Commit the dispatcher slice**

```powershell
git add src/Beacon.HostAgent tests/Beacon.HostAgent.Tests
git commit -m "feat: execute display primitives in Host Agent"
```

### Task 4: Add The Persistent Service-Side Client

**Files:**
- Create: `src/Beacon.Platform.Windows/HostAgent/IHostAgentConnection.cs`
- Create: `src/Beacon.Platform.Windows/HostAgent/HostAgentConnection.cs`
- Create: `src/Beacon.Platform.Windows/HostAgent/HostAgentConnectionState.cs`
- Create: `tests/Beacon.Platform.Windows.Tests/HostAgent/HostAgentConnectionTests.cs`
- Modify: `src/Beacon.Platform.Windows/Beacon.Platform.Windows.csproj`
- Modify: `tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj`

- [ ] **Step 1: Write failing connection tests**

Cover one reader, serialized writes, request-id correlation, out-of-order responses, protocol mismatch, duplicate/unknown responses, canceled caller waits, pipe loss failing pending calls, and event-driven reconnection. Prove there is no retry timer or operation timeout.

```csharp
public interface IHostAgentConnection : IAsyncDisposable
{
    HostAgentConnectionState State { get; }
    Task<HostAgentResponse> SendAsync(
        HostAgentOperation operation,
        object payload,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Run connection tests and verify RED**

Expected: compile failure for missing Host Agent connection types.

- [ ] **Step 3: Implement persistent multiplexed IPC**

Use one background `NamedPipeClientStream.ConnectAsync` governed only by application disposal. Publish connected state only after a versioned `GetStatus` handshake. Use one receive loop, one write semaphore, and a concurrent request-id map. On pipe loss, fail pending requests, mark disconnected, dispose the stream, and immediately await the next pipe instance without a delay loop.

- [ ] **Step 4: Run tests and verify GREEN**

```powershell
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter HostAgentConnection
```

Expected: all connection/generation/disconnect tests pass.

- [ ] **Step 5: Commit the client slice**

```powershell
git add src/Beacon.Platform.Windows tests/Beacon.Platform.Windows.Tests
git commit -m "feat: connect Beacon Service to Host Agent"
```

### Task 5: Proxy The Existing Display Interfaces

**Files:**
- Create: `src/Beacon.Platform.Windows/HostAgent/HostAgentWindowsDisplayApi.cs`
- Create: `tests/Beacon.Platform.Windows.Tests/HostAgent/HostAgentWindowsDisplayApiTests.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`
- Modify: `tests/Beacon.Core.Tests/Architecture/ArchitectureRecoveryBoundaryTests.cs`

- [ ] **Step 1: Write failing proxy and registration tests**

Assert that one proxy implements `IWindowsDisplayApi`, `IWindowsDisplayLeaseSession`, and `IWindowsDisplayNameResolver`; maps every response exactly; caches handshake/lease health without blocking synchronous properties; resolves a logical display name through Host Agent; and is the production registration. Assert that Core, StreamWorker contracts, and Android have no Host Agent references.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj --filter HostAgentWindowsDisplayApi
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter BeaconServiceRegistration
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter ArchitectureRecoveryBoundary
```

Expected: missing proxy and old direct registration failures.

- [ ] **Step 3: Implement and register the proxy**

`HostAgentWindowsDisplayApi` sends typed requests for every existing interface method. `GetDriverStatus` and `Snapshot` return the latest handshake/operation evidence immediately; disconnected state is explicit and non-blocking. `TryResolveDisplayName` uses the latest Host Agent mapping evidence and never falls back to a physical display name. Register one singleton proxy for all three interfaces and remove direct `WindowsDisplayApi` construction from production Server registration.

- [ ] **Step 4: Run focused and display/server tests**

Expected: proxy, registration, architecture, display, and session launch tests pass.

- [ ] **Step 5: Commit the proxy slice**

```powershell
git add src/Beacon.Platform.Windows src/Beacon.Server tests/Beacon.Platform.Windows.Tests tests/Beacon.Server.Tests tests/Beacon.Core.Tests
git commit -m "feat: route display control through Host Agent"
```

### Task 6: Build The No-Console Agent And Installer

**Files:**
- Create: `src/Beacon.HostAgent/Program.cs`
- Create: `src/Beacon.HostAgent/HostAgentOptions.cs`
- Create: `scripts/install-beacon-host-agent.ps1`
- Create: `scripts/uninstall-beacon-host-agent.ps1`
- Create: `tests/Beacon.HostAgent.Tests/HostAgentOptionsTests.cs`
- Create: `tests/Beacon.Core.Tests/Architecture/HostAgentInstallationBoundaryTests.cs`

- [ ] **Step 1: Write failing entry-point and installation-shape tests**

Assert `OutputType=WinExe`, a required owner SID, fixed pipe identity, no network dependencies, no Apollo/Sunshine strings, and scripts that register an interactive-token/highest-run-level task whose action is exactly `Beacon.HostAgent.exe`. Reject script/shell actions, execution limits, and multiple parallel task instances.

- [ ] **Step 2: Run tests and verify RED**

Expected: missing entry point/options/scripts.

- [ ] **Step 3: Implement executable and registration scripts**

`Program` validates that it is Windows, elevated, and running in the configured owning user's interactive session before creating direct display mechanics. It writes sanitized JSONL under `%ProgramData%\\Beacon\\HostAgent\\Logs`. The install script requires elevation, validates the built executable and owning SID, registers one highest-run-level interactive logon task, starts it, and prints exact status. The uninstall script stops and removes only that exact task and process; it leaves driver and user data intact.

- [ ] **Step 4: Build and run static installation tests**

```powershell
dotnet build src\Beacon.HostAgent\Beacon.HostAgent.csproj
dotnet test tests\Beacon.HostAgent.Tests\Beacon.HostAgent.Tests.csproj
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter HostAgentInstallationBoundary
```

Expected: clean build and tests; no task is installed by the test suite.

- [ ] **Step 5: Commit the executable slice**

```powershell
git add src/Beacon.HostAgent scripts/install-beacon-host-agent.ps1 scripts/uninstall-beacon-host-agent.ps1 tests
git commit -m "feat: package interactive Beacon Host Agent"
```

### Task 7: Validate The Display Checkpoint Before Worker Or Emulator

**Files:**
- Modify only if evidence exposes a defect in files owned by Tasks 1-6.
- Record evidence under ignored `.artifacts/host-agent-display-<run-id>`.

- [ ] **Step 1: Run the complete static boundary suite**

```powershell
dotnet test tests\Beacon.HostAgent.Contracts.Tests\Beacon.HostAgent.Contracts.Tests.csproj
dotnet test tests\Beacon.HostAgent.Tests\Beacon.HostAgent.Tests.csproj
dotnet test tests\Beacon.Platform.Windows.Tests\Beacon.Platform.Windows.Tests.csproj
dotnet test tests\Beacon.Server.Tests\Beacon.Server.Tests.csproj --filter "StreamWorker|StreamSession|Display|BeaconServiceRegistration"
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter Architecture
```

Expected: all pass, with no Worker/emulator process.

- [ ] **Step 2: Install Host Agent once**

Build the Agent, invoke the installer through one explicit UAC prompt, then verify task principal, interactive logon type, highest run level, no execution timeout, executable action, process session id, elevation, pipe ACL, and `GetStatus` handshake.

- [ ] **Step 3: Start only Beacon Service and prepare one synthetic client**

Register `stage-zfold7-worker`, mark beacon active, and assert exactly one virtual display at `2560x1600@120`, physical primary retained, mirror false, Worker count zero, and emulator count zero.

- [ ] **Step 4: Validate persistence through the observed GPU topology switch**

Sample topology and Agent lease status at 500 ms while Windows migrates the physical panel from NVIDIA to Intel. Require the same logical virtual display to remain active and mapped after the switch, with no replacement lease and no physical fallback.

- [ ] **Step 5: Validate cleanup**

Mark the client inactive with no owned work. Require display removal, physical `2560x1600` primary verification, mirror false, Agent lease count zero, Worker count zero, and no emulator.

- [ ] **Step 6: Commit any evidence-driven fix, then proceed to Worker validation**

Do not start StreamWorker or the emulator until Steps 1-5 pass. The next runtime action after this plan is the existing isolated Worker startup/display-binding checkpoint.

---

## Plan Self-Review

- The plan covers the first implementation boundary from the Host Agent specification: process placement, strict IPC, ACL/caller identity, display execution, no disconnect cleanup, non-elevated Service proxy, installation, and staged dynamic validation.
- Driver package staging/install/rollback is intentionally excluded and will receive its own plan only after the pipe and display executor are proven. This avoids debugging package installation and topology ownership simultaneously.
- Core, StreamWorker, StreamCore, and public APK contracts remain protocol-neutral and Host Agent-free.
- No step introduces Apollo, Sunshine, GameStream, Moonlight, wrapper, descriptor-file, or alternate Android paths.
- No product timeout, startup sleep, lease timeout, or cancellation watchdog is introduced.
- The plan contains no unresolved placeholder or alternate implementation choice.
