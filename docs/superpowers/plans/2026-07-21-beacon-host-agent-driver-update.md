# Beacon Host Agent Driver Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Beacon install an approved SudoVDA package without another UAC prompt by staging a complete manifest-bound package and asking the elevated Host Agent to execute, verify, and durably record one deterministic driver transaction.

**Architecture:** Beacon Service owns update policy and writes packages beneath the ACL-controlled Host Agent inbox. The Host Agent accepts only a package identifier and transaction identifier over its existing SID-restricted pipe, validates and copies the complete package into protected staging, blocks installation while a lease or virtual path exists, exports the active package for rollback, performs fixed Windows driver operations, and verifies the active device before reporting success. A pipe disconnect cannot cancel the transaction; callers recover the durable result by transaction identifier.

**Tech Stack:** .NET 10, C#, xUnit, named-pipe Host Agent protocol, Windows Setup/PnP device evidence, Authenticode verification, `pnputil.exe` as a fixed privileged driver-store primitive, JSON manifests and journals, PowerShell deployment/acceptance scripts.

---

## File Map

- `src/Beacon.HostAgent.Contracts/HostAgentDriverPayloads.cs`: versioned driver-update request/result and manifest-independent IPC payloads.
- `src/Beacon.HostAgent.Contracts/HostAgentProtocol.cs`: adds only typed driver operations; no command line, executable, or path payloads.
- `src/Beacon.HostAgent/DriverUpdates/SudoVdaPackageManifest.cs`: strict on-disk package manifest model.
- `src/Beacon.HostAgent/DriverUpdates/SudoVdaPackageValidator.cs`: package-id confinement, file-set, hash, INF, architecture, hardware-id, signer, and protocol validation.
- `src/Beacon.HostAgent/DriverUpdates/SudoVdaDriverEvidence.cs`: normalized active/candidate driver evidence.
- `src/Beacon.HostAgent/DriverUpdates/IWindowsSudoVdaDriverPlatform.cs`: test boundary for inventory, package export, install, restart, signature, hash, and protocol operations.
- `src/Beacon.HostAgent/DriverUpdates/WindowsSudoVdaDriverPlatform.cs`: fixed Windows implementation; it never accepts caller-supplied programs or arguments.
- `src/Beacon.HostAgent/DriverUpdates/SudoVdaUpdateJournal.cs`: atomic durable transaction/evidence persistence.
- `src/Beacon.HostAgent/DriverUpdates/SudoVdaUpdateCoordinator.cs`: serialized state machine, independent lifetime, verification, and one rollback.
- `src/Beacon.HostAgent/IHostAgentDriverUpdateExecutor.cs`: narrow dispatcher boundary.
- `src/Beacon.HostAgent/HostAgentDispatcher.cs`: dispatches start/query requests without coupling them to pipe cancellation.
- `src/Beacon.HostAgent/Program.cs`: composes the package validator, journal, Windows platform, and coordinator.
- `src/Beacon.Platform.Windows/HostAgent/HostAgentDriverUpdateClient.cs`: Service-side typed client over the persistent Agent connection.
- `src/Beacon.Server/Api/AdminEndpoints.cs`: authenticated local admin endpoints for package install and transaction lookup.
- `scripts/install-host-agent.ps1`: creates protected `Inbox`, `Staged`, `InstalledEvidence`, and `Logs` directories and grants the owning user only the required inbox access.
- `scripts/new-sudovda-package.ps1`: creates a complete package and manifest from an INF/CAT/binary directory; it never installs anything.
- `scripts/test-host-agent-driver-update.ps1`: static and same-package dynamic acceptance with exact evidence checks.
- `tests/Beacon.HostAgent.Contracts.Tests/HostAgentProtocolTests.cs`: protocol round-trip and unknown-field rejection.
- `tests/Beacon.HostAgent.Tests/SudoVdaPackageValidatorTests.cs`: hostile package validation matrix.
- `tests/Beacon.HostAgent.Tests/SudoVdaUpdateJournalTests.cs`: atomic durability and restart recovery.
- `tests/Beacon.HostAgent.Tests/SudoVdaUpdateCoordinatorTests.cs`: gate, ordering, verification, rollback, and disconnect semantics.
- `tests/Beacon.HostAgent.Tests/HostAgentDispatcherTests.cs`: driver-operation dispatch and sanitized failures.
- `tests/Beacon.Platform.Windows.Tests/HostAgent/HostAgentDriverUpdateClientTests.cs`: Service proxy behavior.
- `tests/Beacon.Server.Tests/AdminApiTests.cs`: local admin authorization and response mapping.

### Task 1: Define The Typed Update Contract

**Files:**
- Create: `src/Beacon.HostAgent.Contracts/HostAgentDriverPayloads.cs`
- Modify: `src/Beacon.HostAgent.Contracts/HostAgentProtocol.cs`
- Modify: `tests/Beacon.HostAgent.Contracts.Tests/HostAgentProtocolTests.cs`

- [ ] **Step 1: Add failing protocol tests**

Add round-trip tests for `InstallStagedSudoVdaPackagePayload`, `QuerySudoVdaUpdatePayload`, and `SudoVdaUpdatePayload`. Assert that package IDs are plain identifiers, transaction IDs are distinct from frame request IDs, enum values serialize as strings, and unknown JSON members fail deserialization.

```csharp
var request = new HostAgentRequest(
    HostAgentProtocol.CurrentVersion,
    Guid.NewGuid(),
    HostAgentOperation.InstallStagedSudoVdaPackage,
    HostAgentProtocol.CreatePayload(
        new InstallStagedSudoVdaPackagePayload("sudovda-22.48.58.193", transactionId)));
HostAgentRequest decoded = HostAgentProtocol.DeserializeRequest(
    HostAgentProtocol.SerializeRequest(request));
Assert.Equal(HostAgentOperation.InstallStagedSudoVdaPackage, decoded.Operation);
```

- [ ] **Step 2: Run the contract tests and confirm the missing-type failure**

Run: `dotnet test tests/Beacon.HostAgent.Contracts.Tests/Beacon.HostAgent.Contracts.Tests.csproj --no-restore`

Expected: FAIL because the driver payloads and operations do not exist.

- [ ] **Step 3: Add the contract types**

Define `InstallStagedSudoVdaPackage`, `QuerySudoVdaUpdate`, and payloads containing only `PackageId` and/or `TransactionId`. Define terminal states `Succeeded`, `RolledBack`, and `Degraded`, plus nonterminal states `Accepted`, `Validating`, `Installing`, `Verifying`, and `RollingBack`. Result evidence contains normalized package/version/INF/protocol/signer/hash/device fields and a sanitized diagnostic.

- [ ] **Step 4: Run the contract tests**

Run: `dotnet test tests/Beacon.HostAgent.Contracts.Tests/Beacon.HostAgent.Contracts.Tests.csproj --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit the contract**

```powershell
git add src/Beacon.HostAgent.Contracts/HostAgentDriverPayloads.cs src/Beacon.HostAgent.Contracts/HostAgentProtocol.cs tests/Beacon.HostAgent.Contracts.Tests/HostAgentProtocolTests.cs
git commit -m "feat: define Host Agent driver update protocol"
```

### Task 2: Validate Complete Manifest-Bound Packages

**Files:**
- Create: `src/Beacon.HostAgent/DriverUpdates/SudoVdaPackageManifest.cs`
- Create: `src/Beacon.HostAgent/DriverUpdates/SudoVdaPackageValidator.cs`
- Create: `src/Beacon.HostAgent/DriverUpdates/SudoVdaDriverEvidence.cs`
- Create: `tests/Beacon.HostAgent.Tests/SudoVdaPackageValidatorTests.cs`

- [ ] **Step 1: Add the hostile-package test matrix**

Create isolated package roots and assert rejection of: invalid package IDs, parent traversal, rooted manifest paths, duplicate paths, alternate data streams, reparse points anywhere below the package root, absent or extra files, hash mismatch, catalog/signature mismatch, non-`NTamd64` INF, provider/class/catalog/binary mismatch, hardware ID other than `Root\\SudoMaker\\SudoVDA`, and a protocol lower than Beacon's required `0.2`. Assert a three-file SudoVDA package plus `manifest.json` is accepted and produces normalized candidate evidence.

- [ ] **Step 2: Run the validator tests and confirm failure**

Run: `dotnet test tests/Beacon.HostAgent.Tests/Beacon.HostAgent.Tests.csproj --no-restore --filter FullyQualifiedName~SudoVdaPackageValidatorTests`

Expected: FAIL because the validator is absent.

- [ ] **Step 3: Implement strict manifest and filesystem validation**

Use `Path.GetFullPath`, `Path.GetRelativePath`, ordinal case-insensitive exact file-set comparison, `FileSystemInfo.LinkTarget`/reparse attributes, stream enumeration for ADS, and streaming SHA-256. Parse the INF as sections/directives rather than searching arbitrary substrings. Keep signature verification behind `IWindowsSudoVdaDriverPlatform` so tests provide deterministic signer evidence. Copy accepted packages into `Staged\\<transaction-id>` only after every source validation passes, then re-hash the copied package.

- [ ] **Step 4: Run validator and full Host Agent tests**

Run: `dotnet test tests/Beacon.HostAgent.Tests/Beacon.HostAgent.Tests.csproj --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit package validation**

```powershell
git add src/Beacon.HostAgent/DriverUpdates tests/Beacon.HostAgent.Tests/SudoVdaPackageValidatorTests.cs
git commit -m "feat: validate staged SudoVDA packages"
```

### Task 3: Persist The Driver Transaction State Machine

**Files:**
- Create: `src/Beacon.HostAgent/DriverUpdates/SudoVdaUpdateJournal.cs`
- Create: `src/Beacon.HostAgent/DriverUpdates/SudoVdaUpdateCoordinator.cs`
- Create: `src/Beacon.HostAgent/IHostAgentDriverUpdateExecutor.cs`
- Create: `tests/Beacon.HostAgent.Tests/SudoVdaUpdateJournalTests.cs`
- Create: `tests/Beacon.HostAgent.Tests/SudoVdaUpdateCoordinatorTests.cs`

- [ ] **Step 1: Add failing journal and coordinator tests**

Cover atomic write-and-replace, duplicate transaction idempotency, conflicting package reuse rejection, process-restart lookup, one operation at a time, Service-reported active leases, Agent-observed leases, Agent-observed virtual paths, happy-path ordering, candidate verification failure followed by verified rollback, rollback verification failure producing `Degraded`, and caller cancellation after acceptance not cancelling the internal transaction.

```csharp
SudoVdaUpdatePayload accepted = coordinator.Start(packageId, transactionId);
callerCancellation.Cancel();
await platform.InstallEntered;
platform.AllowInstall();
SudoVdaUpdatePayload terminal = await coordinator.WaitForTerminalStateAsync(transactionId);
Assert.Equal(SudoVdaUpdateState.Succeeded, terminal.State);
```

- [ ] **Step 2: Run the focused tests and confirm failure**

Run: `dotnet test tests/Beacon.HostAgent.Tests/Beacon.HostAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~SudoVdaUpdateJournalTests|FullyQualifiedName~SudoVdaUpdateCoordinatorTests"`

Expected: FAIL because the journal and coordinator are absent.

- [ ] **Step 3: Implement durable serialized coordination**

`Start` validates identifiers, writes `Accepted` before returning, and starts one Agent-owned task using `CancellationToken.None`. Each transition is atomically persisted. The sequence is validate/copy, independently confirm zero lease and zero virtual paths, capture active evidence, export the active package into `InstalledEvidence\\<transaction-id>`, install candidate, restart the resolved SudoVDA instance, verify every required field, then persist `Succeeded`. Any post-mutation failure performs exactly one rollback and persists `RolledBack` only after full prior-evidence verification; otherwise it persists `Degraded`.

- [ ] **Step 4: Run coordinator and full Host Agent tests**

Run: `dotnet test tests/Beacon.HostAgent.Tests/Beacon.HostAgent.Tests.csproj --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit the transaction engine**

```powershell
git add src/Beacon.HostAgent/DriverUpdates src/Beacon.HostAgent/IHostAgentDriverUpdateExecutor.cs tests/Beacon.HostAgent.Tests/SudoVdaUpdateJournalTests.cs tests/Beacon.HostAgent.Tests/SudoVdaUpdateCoordinatorTests.cs
git commit -m "feat: add durable SudoVDA update transactions"
```

### Task 4: Implement Fixed Windows Driver Operations

**Files:**
- Create: `src/Beacon.HostAgent/DriverUpdates/IWindowsSudoVdaDriverPlatform.cs`
- Create: `src/Beacon.HostAgent/DriverUpdates/WindowsSudoVdaDriverPlatform.cs`
- Modify: `src/Beacon.Platform.Windows/Displays/IWindowsDisplayApi.cs`
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayApi.cs`
- Modify: `src/Beacon.HostAgent/WindowsHostAgentDisplayExecutor.cs`
- Create: `tests/Beacon.HostAgent.Tests/WindowsSudoVdaDriverPlatformTests.cs`
- Modify: `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayApiTests.cs`

- [ ] **Step 1: Add tests for fixed command construction and evidence normalization**

Assert that the platform resolves only SudoMaker display devices matching `root\\sudomaker\\sudovda`, invokes only `%SystemRoot%\\System32\\pnputil.exe`, and constructs only fixed `export-driver`, `add-driver /install`, `restart-device`, and rollback `delete-driver /uninstall` operations from internally validated paths and published INF names. Assert malformed INF names, device IDs, and paths cannot reach process launch.

- [ ] **Step 2: Expose structured SudoVDA protocol evidence**

Expand `DisplayDriverStatus` with nullable protocol major/minor/incremental fields populated directly by `WindowsDisplayApi`. Update call sites and tests so compatibility checks no longer parse diagnostics.

- [ ] **Step 3: Implement the Windows platform**

Use structured PnP/Setup device properties for active instance, published INF, provider, version, and problem code. Use WinVerifyTrust/certificate APIs for catalog and DLL signer/thumbprint evidence and SHA-256 for the active UMDF binary. Run fixed `pnputil.exe` operations with argument lists and wait for natural process completion; cancellation does not kill `pnputil`. Treat process exit as operation evidence, while final success comes only from re-querying device/protocol/signature/hash state.

- [ ] **Step 4: Run Platform and Host Agent suites**

Run: `dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --no-restore`

Run: `dotnet test tests/Beacon.HostAgent.Tests/Beacon.HostAgent.Tests.csproj --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit Windows driver primitives**

```powershell
git add src/Beacon.HostAgent/DriverUpdates src/Beacon.Platform.Windows/Displays src/Beacon.HostAgent/WindowsHostAgentDisplayExecutor.cs tests/Beacon.HostAgent.Tests tests/Beacon.Platform.Windows.Tests
git commit -m "feat: add verified Windows SudoVDA operations"
```

### Task 5: Expose Start And Durable Query Through The Existing Pipe

**Files:**
- Modify: `src/Beacon.HostAgent/HostAgentDispatcher.cs`
- Modify: `src/Beacon.HostAgent/Program.cs`
- Modify: `tests/Beacon.HostAgent.Tests/HostAgentDispatcherTests.cs`
- Modify: `tests/Beacon.HostAgent.Tests/HostAgentConnectionSessionTests.cs`

- [ ] **Step 1: Add failing dispatcher and disconnect tests**

Assert start returns the durable accepted snapshot, query returns current/terminal snapshots, unknown transactions return `driver-update-not-found`, a second active transaction returns `driver-update-busy`, package diagnostics contain no protected absolute paths, and closing the request pipe after acceptance does not cancel coordinator execution.

- [ ] **Step 2: Run focused tests and confirm failure**

Run: `dotnet test tests/Beacon.HostAgent.Tests/Beacon.HostAgent.Tests.csproj --no-restore --filter "FullyQualifiedName~HostAgentDispatcherTests|FullyQualifiedName~HostAgentConnectionSessionTests"`

Expected: FAIL for unsupported driver operations.

- [ ] **Step 3: Dispatch through the driver executor**

Inject `IHostAgentDriverUpdateExecutor` beside the display executor. Pass request cancellation only through payload parsing and durable acceptance; the coordinator owns transaction execution after acceptance. `Program` creates the protected roots, journal, platform, validator, and coordinator once for the process lifetime.

- [ ] **Step 4: Run Contracts and Host Agent suites**

Run: `dotnet test tests/Beacon.HostAgent.Contracts.Tests/Beacon.HostAgent.Contracts.Tests.csproj --no-restore`

Run: `dotnet test tests/Beacon.HostAgent.Tests/Beacon.HostAgent.Tests.csproj --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit the Agent endpoint**

```powershell
git add src/Beacon.HostAgent tests/Beacon.HostAgent.Tests
git commit -m "feat: expose SudoVDA updates through Host Agent"
```

### Task 6: Add Service-Side Admin Control

**Files:**
- Create: `src/Beacon.Platform.Windows/HostAgent/HostAgentDriverUpdateClient.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`
- Create: `tests/Beacon.Platform.Windows.Tests/HostAgent/HostAgentDriverUpdateClientTests.cs`
- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs`
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`

- [ ] **Step 1: Add failing proxy and admin API tests**

Assert an authenticated local admin can submit a package ID plus transaction ID and query it, unauthenticated callers are rejected, malformed IDs return validation errors, Host Agent unavailable maps to a service-unavailable response, busy maps to conflict, and durable terminal evidence is returned unchanged.

- [ ] **Step 2: Run focused tests and confirm failure**

Run: `dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --no-restore --filter FullyQualifiedName~HostAgentDriverUpdateClientTests`

Run: `dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --no-restore --filter "FullyQualifiedName~AdminApiTests|FullyQualifiedName~BeaconServiceRegistrationTests"`

Expected: FAIL because the proxy and routes do not exist.

- [ ] **Step 3: Implement typed client and local admin routes**

Register one `HostAgentDriverUpdateClient` using the existing persistent `IHostAgentConnection`. Add `POST /admin/driver/sudovda/updates` and `GET /admin/driver/sudovda/updates/{transactionId}` using existing admin authentication. The request never contains a path, command, process, or device identifier.

- [ ] **Step 4: Run Platform and Server suites**

Run: `dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --no-restore`

Run: `dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit Service control**

```powershell
git add src/Beacon.Platform.Windows/HostAgent src/Beacon.Server tests/Beacon.Platform.Windows.Tests tests/Beacon.Server.Tests
git commit -m "feat: add SudoVDA update admin control"
```

### Task 7: Install Storage ACLs And Package Tooling

**Files:**
- Modify: `scripts/install-host-agent.ps1`
- Create: `scripts/new-sudovda-package.ps1`
- Create: `scripts/test-host-agent-driver-update.ps1`
- Modify: `tests/Beacon.HostAgent.Tests/HostAgentOptionsTests.cs`

- [ ] **Step 1: Add script contract checks**

Extend the script acceptance tests to assert idempotent creation of `%ProgramData%\\Beacon\\HostAgent\\Inbox`, `Staged`, `InstalledEvidence`, and `Logs`; inherited broad write access is removed from protected directories; the owning user can write package directories only below `Inbox`; and neither script accepts executable or driver-operation arguments.

- [ ] **Step 2: Update installer and package builder**

The installer records the four roots in fixed Host Agent arguments and applies explicit SYSTEM/Administrators full control plus owning-user inbox modify access. `new-sudovda-package.ps1` validates exactly one INF, its declared CAT, and copied binaries, computes hashes, captures signer identity and architecture, receives an explicit compatible protocol version, emits schema version 1 `manifest.json`, and copies atomically into `Inbox\\<package-id>`.

- [ ] **Step 3: Add acceptance script**

The acceptance script checks zero Agent leases and physical-only topology, packages the current `SudoVDA-watchdog` output, submits the update, polls the durable transaction endpoint without using a cancellation timeout, and verifies terminal evidence. For the first dynamic run, the package equals active `oem163.inf`; accepted success must prove the same active INF/version/protocol/signer/hash and preserve physical-only topology.

- [ ] **Step 4: Run static checks**

Run: `pwsh -NoProfile -File scripts/test-host-agent-driver-update.ps1 -StaticOnly`

Expected: PASS without changing the driver or display topology.

- [ ] **Step 5: Commit deployment tooling**

```powershell
git add scripts/install-host-agent.ps1 scripts/new-sudovda-package.ps1 scripts/test-host-agent-driver-update.ps1 tests/Beacon.HostAgent.Tests/HostAgentOptionsTests.cs
git commit -m "feat: deploy Host Agent driver update storage"
```

### Task 8: Dynamic Validation, Cleanup, And Synchronization

**Files:**
- Modify only when a validation defect requires a focused fix.

- [ ] **Step 1: Establish the safe baseline**

Confirm Apollo is untouched, no Beacon Server/Worker/emulator process is running, Agent lease count is zero, topology contains one physical primary path and no virtual path, SudoVDA device problem code is zero, and active evidence is `ROOT\\DISPLAY\\0000`, `oem163.inf`, `22.48.58.193`, protocol-compatible, signer-valid, and binary hash `BD26C518007370BD3FA66C62D7332AFAE00382041D52277C4C4BAE9A7334FF03`.

- [ ] **Step 2: Publish and reinstall the Host Agent once**

Run the repository's Host Agent publish command and elevated installer. Verify task `Beacon Stream Host Agent` runs at highest level in the owning interactive session and the installed binary hashes match publish output.

- [ ] **Step 3: Validate the current-package transaction**

Run: `pwsh -NoProfile -File scripts/test-host-agent-driver-update.ps1`

Expected: one terminal `Succeeded` transaction with candidate and active evidence equal; no extra virtual display; physical display remains primary; device problem code remains zero; reconnecting the Service retrieves the same durable result.

- [ ] **Step 4: Validate package rejection and active-display blocking dynamically**

Submit a hash-corrupted copy and assert rejection before any driver operation. Create one Beacon test display through the Agent, submit the valid package and assert the update is blocked, then explicitly remove the test display and restore physical-only topology. Do not touch Apollo.

- [ ] **Step 5: Run the full static and dynamic gates**

Run all Contracts, Core, Platform Windows, Host Agent, Server, DisplayProbe, StreamWorker, native, and Android static suites. Then repeat the ordered dynamic path: one extra desktop, environment verification, Worker capture, emulator APK session, explicit teardown, and physical-only topology verification.

- [ ] **Step 6: Refactor and repeat all gates**

Remove duplication exposed by the first pass without widening behavior. Re-run the exact static and dynamic commands from Step 5 and compare the resulting evidence.

- [ ] **Step 7: Commit, push, and update the existing pull request**

```powershell
git status --short
git push origin codex/beacon-production-benchmarks
gh pr view 142 --json url,state,headRefName,statusCheckRollup
```

Expected: all driver-update commits are on PR #142, required checks are green, no temporary driver package is tracked, no process owns a test display, and the Windows topology is physical-only.

## Self-Review

- Spec coverage: Tasks 1-8 cover `REQ-HOST-002`, `REQ-HOST-006`, and `REQ-HOST-009` through `REQ-HOST-013`, including complete packages, independent install gates, post-install evidence, one rollback, degraded state, and disconnect-independent execution.
- Boundary check: Core, StreamWorker, StreamCore, and the APK receive no Host Agent or driver-update contract. Service owns authorization and policy; Agent owns privileged mechanics.
- Safety check: no loose DLL installation, arbitrary command execution, caller-selected path/device, passive inbox watcher, update cancellation timeout, stream-disconnect cleanup, or unverified success path is introduced.
- Type consistency: IPC uses `PackageId` and `TransactionId`; package paths remain Agent-local; transaction states and evidence are shared payloads; package manifests remain private to the Agent.
- Dynamic sequencing: same-package validation occurs before any changed driver package, and active-display rejection is proven before the full stream regression pass.
