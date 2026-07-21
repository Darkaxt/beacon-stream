# Beacon Host Agent Self-Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Install one stable elevated bootstrap so CI-signed Host Agent versions can be staged, activated, verified, queried, and rolled back without another interactive UAC prompt.

**Architecture:** A protected, single-file bootstrap supervises versioned Host Agent children and independently verifies every candidate package with an embedded ECDSA public key. The running Agent accepts only package and transaction identifiers, stages the signed package into protected storage, flushes its response, and exits with a dedicated code; the bootstrap owns activation, authenticated readiness, rollback, and durable final state.

**Tech Stack:** .NET 10 on Windows, C# named pipes and process APIs, ECDSA P-256/SHA-256, xUnit, PowerShell, GitHub Actions, `gh` CLI.

---

## File Structure

- `src/Beacon.HostAgent.Update/`: shared manifest, signature, package-tree, journal, state-file, and path-confinement primitives. The bootstrap embeds this code into its stable single-file publish; the Agent uses it only for early validation and staging.
- `src/Beacon.HostAgent.Bootstrap/`: immutable process supervisor, child readiness authentication, activation state machine, and rollback owner.
- `src/Beacon.HostAgent.Control/`: unelevated, owner-scoped command-line client for typed install/query requests.
- `src/Beacon.HostAgent/HostUpdates/`: Agent-side protected staging coordinator and typed dispatcher adapter.
- `tests/Beacon.HostAgent.Update.Tests/`: package and durable-state contract tests.
- `tests/Beacon.HostAgent.Bootstrap.Tests/`: deterministic supervisor and rollback tests behind a process/storage abstraction.
- `tests/Beacon.HostAgent.Tests/`: response-flush ordering, readiness notification, and dispatch tests.
- `tests/Beacon.HostAgent.Control.Tests/`: command parsing and owner-pipe request tests.
- `scripts/build-host-agent-update.ps1`: deterministic local package construction/verification entry point used by CI.
- `scripts/update-host-agent.ps1`: dispatch, download, stage, request, reconnect, and evidence verification flow.
- `scripts/install-host-agent.ps1`: one-time elevated bootstrap/task/ACL migration and initial-version install.
- `.github/workflows/host-agent-update.yml`: manually dispatched signed package producer.

### Task 1: Signed Package Contract

**Files:**
- Create: `src/Beacon.HostAgent.Update/Beacon.HostAgent.Update.csproj`
- Create: `src/Beacon.HostAgent.Update/HostAgentUpdateManifest.cs`
- Create: `src/Beacon.HostAgent.Update/HostAgentUpdatePackageValidator.cs`
- Create: `src/Beacon.HostAgent.Update/HostAgentUpdatePackageBuilder.cs`
- Create: `tests/Beacon.HostAgent.Update.Tests/Beacon.HostAgent.Update.Tests.csproj`
- Create: `tests/Beacon.HostAgent.Update.Tests/HostAgentUpdatePackageValidatorTests.cs`
- Modify: `Beacon.slnx`

- [ ] **Step 1: Write failing package-validation tests**

Cover a valid ECDSA P-256 signature over exact manifest bytes plus rejection of an invalid signature, unknown JSON field, wrong target/architecture, newer minimum bootstrap, undeclared entry point, rooted/traversing/case-colliding paths, extra/missing files, wrong length/hash, reparse points, and alternate data streams. The wished-for API is:

```csharp
var validator = new HostAgentUpdatePackageValidator(publicKeyPem, new Version(1, 0, 0));
HostAgentValidatedPackage package = await validator.ValidateAsync(root, CancellationToken.None);
Assert.Equal("agent-abc123", package.Manifest.PackageId);
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/Beacon.HostAgent.Update.Tests/Beacon.HostAgent.Update.Tests.csproj --no-restore`

Expected: compile failure because the update project and validation types do not exist.

- [ ] **Step 3: Implement strict manifest and tree validation**

Use `JsonUnmappedMemberHandling.Disallow`, exact raw `manifest.json` bytes for signature verification, ordinal-ignore-case duplicate detection, `Path.GetFullPath` confinement, `FileAttributes.ReparsePoint` rejection, Windows stream enumeration, exact file-set equality, byte-length comparison, and SHA-256 comparison. Expose no executable path or command in the manifest API beyond the fixed normalized entry-point field.

- [ ] **Step 4: Add deterministic builder tests and implementation**

The builder sorts normalized payload paths ordinally and serializes the same typed manifest options used by validation:

```csharp
HostAgentBuiltPackage built = await HostAgentUpdatePackageBuilder.BuildAsync(
    payloadRoot,
    packageRoot,
    new HostAgentUpdateBuildIdentity(packageId, sourceCommit, "1.0.0"),
    privateKeyPem,
    CancellationToken.None);
```

Assert two manifests from identical inputs are byte-identical and independently validate with the public key.

- [ ] **Step 5: Run package tests and the full managed build**

Run:

```powershell
dotnet test tests/Beacon.HostAgent.Update.Tests/Beacon.HostAgent.Update.Tests.csproj
dotnet build Beacon.slnx -warnaserror
```

Expected: all package tests pass and the solution builds without warnings.

- [ ] **Step 6: Commit and push the package contract**

Stage only the new update project/tests and their `Beacon.slnx` hunks. Commit `feat: validate signed Host Agent packages` and push the current branch.

### Task 2: Durable Agent-Side Staging

**Files:**
- Create: `src/Beacon.HostAgent.Contracts/HostAgentUpdatePayloads.cs`
- Modify: `src/Beacon.HostAgent.Contracts/HostAgentProtocol.cs`
- Create: `src/Beacon.HostAgent/HostUpdates/HostAgentUpdateStorage.cs`
- Create: `src/Beacon.HostAgent/HostUpdates/HostAgentUpdateJournal.cs`
- Create: `src/Beacon.HostAgent/HostUpdates/HostAgentUpdateCoordinator.cs`
- Create: `src/Beacon.HostAgent/IHostAgentUpdateExecutor.cs`
- Modify: `src/Beacon.HostAgent/HostAgentDispatcher.cs`
- Modify: `src/Beacon.HostAgent/Beacon.HostAgent.csproj`
- Modify: `tests/Beacon.HostAgent.Contracts.Tests/HostAgentProtocolTests.cs`
- Modify: `tests/Beacon.HostAgent.Tests/HostAgentDispatcherTests.cs`
- Create: `tests/Beacon.HostAgent.Tests/HostAgentUpdateCoordinatorTests.cs`

- [ ] **Step 1: Write failing typed-contract tests**

Add `InstallStagedHostAgentPackage` and `QueryHostAgentUpdate` operations with identifier-only payloads. Assert unknown path/command fields are rejected and these durable states round-trip: `accepted`, `validating`, `staged`, `installing`, `awaitingReadiness`, `succeeded`, `rolledBack`, `degraded`.

- [ ] **Step 2: Run contract tests and verify RED**

Run: `dotnet test tests/Beacon.HostAgent.Contracts.Tests/Beacon.HostAgent.Contracts.Tests.csproj`

Expected: compile failure for missing Host Agent update contracts.

- [ ] **Step 3: Implement contracts and coordinator tests**

The coordinator API must be synchronous at the handoff boundary after completing all protected staging:

```csharp
HostAgentUpdatePayload Stage(string packageId, Guid transactionId);
bool TryGet(Guid transactionId, out HostAgentUpdatePayload? value);
```

Tests prove idempotent same-transaction replay, transaction/package conflict, one active staged transaction, inbox validation, copy to `StagedHostAgent/<transaction-id>.staging`, protected-copy revalidation, atomic directory rename, atomic journal writes, and `pending-update.json` creation.

- [ ] **Step 4: Implement protected staging and dispatcher routing**

Use the shared package validator both before and after copy. Persist the exact source commit, package id, previous selected version, protected package root, transaction id, and `staged` state. Dispatcher failures use stable result codes and never accept a filesystem path.

- [ ] **Step 5: Run focused and managed tests**

Run:

```powershell
dotnet test tests/Beacon.HostAgent.Contracts.Tests/Beacon.HostAgent.Contracts.Tests.csproj
dotnet test tests/Beacon.HostAgent.Tests/Beacon.HostAgent.Tests.csproj
dotnet build Beacon.slnx -warnaserror
```

- [ ] **Step 6: Commit and push durable staging**

Commit `feat: stage signed Host Agent updates` with only contract, Agent staging, tests, and required project hunks.

### Task 3: Response-Flush Handoff And Agent Readiness

**Files:**
- Modify: `src/Beacon.HostAgent/HostAgentConnectionSession.cs`
- Modify: `src/Beacon.HostAgent/HostAgentPipeServer.cs`
- Modify: `src/Beacon.HostAgent/HostAgentDispatcher.cs`
- Modify: `src/Beacon.HostAgent/HostAgentOptions.cs`
- Modify: `src/Beacon.HostAgent/Program.cs`
- Create: `src/Beacon.HostAgent/BootstrapReadinessClient.cs`
- Modify: `tests/Beacon.HostAgent.Tests/HostAgentConnectionSessionTests.cs`
- Modify: `tests/Beacon.HostAgent.Tests/HostAgentPipeServerTests.cs`
- Modify: `tests/Beacon.HostAgent.Tests/HostAgentOptionsTests.cs`
- Create: `tests/Beacon.HostAgent.Tests/BootstrapReadinessClientTests.cs`

- [ ] **Step 1: Write failing response-order tests**

Use an observing stream and a `TaskCompletionSource` to assert the exit request remains unset until `HostAgentFrameCodec.WriteResponseAsync` has completed and flushed. A normal request must keep the session alive.

- [ ] **Step 2: Run session tests and verify RED**

Run: `dotnet test tests/Beacon.HostAgent.Tests/Beacon.HostAgent.Tests.csproj --filter FullyQualifiedName~HostAgentConnectionSessionTests`

Expected: failure because dispatch results cannot request post-response shutdown.

- [ ] **Step 3: Implement a typed dispatch outcome**

Introduce an internal result equivalent to:

```csharp
internal sealed record HostAgentDispatchOutcome(
    HostAgentResponse Response,
    HostAgentPostResponseAction Action);
```

The connection session writes and flushes `Response`, then invokes the action callback. The pipe server stops accepting clients and returns `HostAgentExitCodes.ApplyUpdate` only for the successful staged-update outcome.

- [ ] **Step 4: Write and implement readiness tests**

Extend options with required bootstrap mode arguments only when supplied:

```text
--owner-sid <SID> --bootstrap-ready-pipe <pipe> --version-id <package-id>
```

Signal readiness after the owner-only Agent pipe listener exists. The client writes one bounded typed frame containing version id and process id; it has no retry timer and propagates pipe failure as startup failure.

- [ ] **Step 5: Run Agent tests and build**

Run:

```powershell
dotnet test tests/Beacon.HostAgent.Tests/Beacon.HostAgent.Tests.csproj
dotnet build src/Beacon.HostAgent/Beacon.HostAgent.csproj -warnaserror
```

- [ ] **Step 6: Commit and push handoff behavior**

Commit `feat: hand Host Agent updates to bootstrap`.

### Task 4: Stable Bootstrap And Rollback

**Files:**
- Create: `src/Beacon.HostAgent.Bootstrap/Beacon.HostAgent.Bootstrap.csproj`
- Create: `src/Beacon.HostAgent.Bootstrap/Program.cs`
- Create: `src/Beacon.HostAgent.Bootstrap/BootstrapOptions.cs`
- Create: `src/Beacon.HostAgent.Bootstrap/BootstrapStorage.cs`
- Create: `src/Beacon.HostAgent.Bootstrap/BootstrapSupervisor.cs`
- Create: `src/Beacon.HostAgent.Bootstrap/IBootstrapPlatform.cs`
- Create: `src/Beacon.HostAgent.Bootstrap/WindowsBootstrapPlatform.cs`
- Create: `src/Beacon.HostAgent.Bootstrap/AuthenticatedReadinessServer.cs`
- Create: `tests/Beacon.HostAgent.Bootstrap.Tests/Beacon.HostAgent.Bootstrap.Tests.csproj`
- Create: `tests/Beacon.HostAgent.Bootstrap.Tests/BootstrapSupervisorTests.cs`
- Create: `tests/Beacon.HostAgent.Bootstrap.Tests/AuthenticatedReadinessServerTests.cs`
- Modify: `Beacon.slnx`

- [ ] **Step 1: Write failing supervisor state-machine tests**

With a fake `IBootstrapPlatform`, prove current-version launch/readiness, update-exit detection, independent staged-package revalidation, fresh `.pending` version install, atomic pointer switch, candidate readiness success, candidate pre-readiness exit rollback exactly once, previous readiness `rolledBack`, and previous pre-readiness exit `degraded`.

- [ ] **Step 2: Run bootstrap tests and verify RED**

Run: `dotnet test tests/Beacon.HostAgent.Bootstrap.Tests/Beacon.HostAgent.Bootstrap.Tests.csproj --no-restore`

Expected: compile failure because bootstrap types do not exist.

- [ ] **Step 3: Implement the event-driven supervisor**

Model child startup as two competing events only:

```csharp
Task completed = await Task.WhenAny(readiness, child.Exit).ConfigureAwait(false);
```

Do not add `Task.Delay`, startup timers, process-kill cancellation, or sleeps. Once ready, await normal child exit. An update is eligible only when exit code equals the dedicated constant and protected pending state matches a staged transaction.

- [ ] **Step 4: Write and implement authenticated readiness tests**

The Windows server creates a one-use ACL-restricted named pipe before launch, calls `GetNamedPipeClientProcessId` after connection, and accepts only the exact launched child PID plus matching version id. Tests reject a different PID/version and verify the pipe cannot authorize another launch.

- [ ] **Step 5: Implement protected activation and rollback storage**

Copy payload into `<package-id>.pending`, validate the copy, rename to `<package-id>`, atomically replace `current-version.json`, and journal each transition with write-through files. Retain only current and one prior verified version after candidate readiness.

- [ ] **Step 6: Run bootstrap tests and publish the trust root**

Run:

```powershell
dotnet test tests/Beacon.HostAgent.Bootstrap.Tests/Beacon.HostAgent.Bootstrap.Tests.csproj
dotnet publish src/Beacon.HostAgent.Bootstrap/Beacon.HostAgent.Bootstrap.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Expected: tests pass and publish produces one bootstrap executable plus symbols/configuration evidence only.

- [ ] **Step 7: Commit and push the bootstrap**

Commit `feat: supervise versioned Host Agent processes`.

### Task 5: Unelevated Typed Control Client

**Files:**
- Create: `src/Beacon.HostAgent.Control/Beacon.HostAgent.Control.csproj`
- Create: `src/Beacon.HostAgent.Control/Program.cs`
- Create: `src/Beacon.HostAgent.Control/HostAgentControlOptions.cs`
- Create: `src/Beacon.HostAgent.Control/HostAgentControlClient.cs`
- Create: `tests/Beacon.HostAgent.Control.Tests/Beacon.HostAgent.Control.Tests.csproj`
- Create: `tests/Beacon.HostAgent.Control.Tests/HostAgentControlOptionsTests.cs`
- Create: `tests/Beacon.HostAgent.Control.Tests/HostAgentControlClientTests.cs`
- Modify: `Beacon.slnx`

- [ ] **Step 1: Write failing parser and request tests**

Support only:

```text
install --package-id <id> --transaction-id <guid>
query --transaction-id <guid>
status
```

Reject path, command, executable, shell, and unknown arguments. Verify the client uses the current owner SID pipe and prints one JSON result suitable for PowerShell parsing.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/Beacon.HostAgent.Control.Tests/Beacon.HostAgent.Control.Tests.csproj --no-restore`

- [ ] **Step 3: Implement direct owner-pipe control**

Create a `NamedPipeClientStream` from `HostAgentPipeName.Create(ownerSid)`, send one typed request, read its correlated response, emit JSON, and exit nonzero for protocol or operation failure. No network listener or arbitrary privileged operation is introduced.

- [ ] **Step 4: Run tests and full managed build**

Run:

```powershell
dotnet test tests/Beacon.HostAgent.Control.Tests/Beacon.HostAgent.Control.Tests.csproj
dotnet build Beacon.slnx -warnaserror
```

- [ ] **Step 5: Commit and push control client**

Commit `feat: control Host Agent updates without elevation`.

### Task 6: CI Signing And One-Time Installer Migration

**Files:**
- Create: `build/host-agent-update-public.pem`
- Create: `src/Beacon.HostAgent.Package/Beacon.HostAgent.Package.csproj`
- Create: `src/Beacon.HostAgent.Package/Program.cs`
- Create: `scripts/build-host-agent-update.ps1`
- Create: `scripts/initialize-host-agent-update-key.ps1`
- Create: `scripts/update-host-agent.ps1`
- Create: `.github/workflows/host-agent-update.yml`
- Modify: `scripts/install-host-agent.ps1`
- Create: `tests/scripts/test_host_agent_update_pipeline.py`
- Modify: `Beacon.slnx`

- [ ] **Step 1: Write failing static pipeline tests**

Parse workflow and scripts to assert: workflow dispatch uses request id; secret is consumed only by the package signer; artifact name includes request id and commit; local updater rejects dirty/uncommitted source; no `RunAs`, unsigned fallback, direct Program Files write, timeout cancellation, or arbitrary package path exists in the updater; installer task action points to bootstrap.

- [ ] **Step 2: Run script tests and verify RED**

Run: `python -m unittest tests.scripts.test_host_agent_update_pipeline`

- [ ] **Step 3: Implement in-memory key initialization and signer CLI**

The initialization script generates P-256 key material in memory, pipes the base64 private PEM to `gh secret set BEACON_HOST_AGENT_UPDATE_SIGNING_KEY_PEM_B64`, writes only the public PEM, clears managed references, and never prints private material. The signer CLI has fixed `build` and `verify` modes and delegates all validation to `Beacon.HostAgent.Update`.

- [ ] **Step 4: Implement workflow and unattended updater**

The workflow checks out the selected ref, tests relevant projects, publishes `win-x64`, signs and independently verifies the artifact, and uploads it. The updater dispatches the workflow, observes it with heartbeat output, downloads the exact request-id artifact, validates locally, copies only that package into the owner-writable inbox, submits typed ids, reconnects by observing process/pipe state, queries durable state, and verifies task action/parent/hash evidence. It never falls back to UAC.

- [ ] **Step 5: Migrate the elevated installer**

Publish the bootstrap as a stable self-contained single file and Agent as a versioned payload. Install protected bootstrap, version, state, staging, transaction, and inbox directories with explicit ACLs. Initialize current-version state, change the scheduled-task action to bootstrap, and start it. Preserve the installer as the only bootstrap/key recovery path.

- [ ] **Step 6: Run static pipeline and managed validation**

Run:

```powershell
python -m unittest tests.scripts.test_host_agent_update_pipeline
dotnet test Beacon.slnx
dotnet format Beacon.slnx --verify-no-changes --no-restore
git diff --check
```

- [ ] **Step 7: Initialize the repository signing key and dispatch a package build**

Run the key initializer once, commit only the public key and pipeline files, push, dispatch the workflow, and independently verify the downloaded package with the committed public key.

- [ ] **Step 8: Commit and push deployment automation**

Commit `feat: deploy signed Host Agent updates unattended`.

### Task 7: Dynamic Bootstrap And Unattended Update Acceptance

**Files:**
- Create: `scripts/test-host-agent-self-update.ps1`
- Modify: `docs/windows-display-backend.md`
- Modify: `docs/superpowers/specs/2026-07-21-host-agent-self-update-design.md` only if implementation evidence requires a factual correction

- [ ] **Step 1: Capture pre-migration evidence**

Record scheduled-task action, current Agent/bootstrap processes and parent ids, installed hashes, current owner SID/session, and absence/presence of `consent.exe`. Do not change display topology.

- [ ] **Step 2: Perform the one final UAC migration**

Run `scripts/install-host-agent.ps1` once and accept the one bootstrap trust-root prompt. Verify the task action targets `Beacon.HostAgent.Bootstrap.exe`, bootstrap is elevated in the interactive session, and the versioned Agent is its child and responds to `status`.

- [ ] **Step 3: Prove a signed unattended same-version update**

Run `scripts/update-host-agent.ps1`. Evidence must show one CI run/artifact/source SHA, no new `consent.exe`, staged and installed hashes matching the manifest, `succeeded` transaction state, pointer/package agreement, bootstrap parentage, and a successful post-restart status response.

- [ ] **Step 4: Prove deterministic rollback**

Use a CI-signed test candidate whose Agent exits before readiness under an explicit test-only build property. Verify exactly one previous-version restart, `rolledBack`, restored pointer/hash/status, and no UAC. Do not ship the failing candidate as current.

- [ ] **Step 5: Resume the original runtime sequence**

Deploy the signed Agent containing the pending same-handle SudoVDA fix through the unattended pipeline. Then test in order: virtual display creation/persistence, environment health, emulator client, production stream, disconnect/restore. Never start later stages when the display stage fails.

- [ ] **Step 6: Refactor and rerun all gates**

Remove duplication while keeping tests green, then rerun managed tests/build/format, native tests, Android tests, package verification, unattended update acceptance, and sequential display/emulator/stream acceptance.

- [ ] **Step 7: Final sync**

Commit validated runtime evidence and documentation, push the branch, update PR #142, and report exact test counts, workflow run id, transaction id, package/source hashes, installed process ancestry, and remaining risks.
