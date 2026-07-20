# Beacon Host Agent Design

Status: approved architecture extension, 2026-07-21

## Purpose

Beacon needs a stable Windows boundary for operations that require an elevated token or reliable access to the interactive console desktop. Repeated UAC prompts are not a lifecycle mechanism, and running the whole Beacon Service elevated would unnecessarily enlarge the privileged attack surface.

`Beacon.HostAgent` is a bundled, headless Beacon component installed once for the owning Windows user. It exposes a local, ACL-restricted named pipe and executes only versioned, typed host operations. Beacon Service remains the sole policy and lifecycle owner. Host Agent never plans a session, selects a display policy, launches a game, starts StreamWorker, accepts remote traffic, or infers cleanup from pipe disconnection.

The first implementation unblocks verified display control. Driver package staging and updates use the same boundary after the display path is validated.

Version-one scope excludes remote administration, arbitrary elevated command execution, Host Agent self-update, multiple simultaneous Windows users, application/process management, and any streaming responsibility.

## Process Model

Beacon version one uses four production process roles:

1. **Beacon Service** runs non-elevated as the owning user in the interactive Windows session and controls registration, policy, planning, leases, application ownership, StreamWorker, recovery, and the client API.
2. **Beacon Host Agent** runs elevated in that same user's interactive session and executes a fixed set of Windows and driver primitives.
3. **Beacon StreamWorker** runs headless and policy-free under Beacon Service ownership.
4. **Beacon Cockpit** is the local WPF UI and uses Beacon Service APIs; it does not bypass policy by calling Host Agent directly.

Host Agent is registered once through an installer-created Task Scheduler entry with these properties:

- Principal: the owning user's SID.
- Logon type: interactive token.
- Run level: highest available.
- Trigger: user logon.
- Multiple instances: ignore new.
- Executable: the installed no-console `Beacon.HostAgent.exe`, never a script or shell wrapper.
- Runtime: unbounded. Product lifecycle is event-driven and is not canceled by a task timeout.

An interactive elevated process is intentional. A session-zero Windows service cannot safely own Current Control Display operations because `SetDisplayConfig` requires access to the current console desktop. Host Agent is not a tray application and shows no routine UI.

## Ownership Boundary

Beacon Service owns:

- Desired display state and per-client lease identity.
- The inactive-client **AND** no-owned-work cleanup decision.
- Session ordering, compensation, and recovery policy.
- Package selection and the decision to request a driver update.
- User-visible diagnostics and operational history.

Host Agent owns only execution mechanics:

- Validate access to the active Windows display topology.
- Activate a verified virtual display while preserving a physical path.
- Set a verified display primary.
- restore a verified physical display primary;
- Apply extended topology as a repair primitive.
- Apply Windows Advanced Color state when supported.
- Query the installed SudoVDA package, device, protocol, and active binary identity.
- Stage, verify, install, restart, validate, and roll back an approved SudoVDA package.

Host Agent does not remove a display, stop a stream, terminate an application, or restore topology merely because Beacon Service disconnects. A pipe disconnect is not evidence that the client is inactive or that owned work is empty.

## Local IPC

The pipe contract is Beacon-owned and versioned independently of the public client protocol.

- Pipe name includes the owning user SID and a fixed Beacon product identifier.
- The pipe ACL grants access only to `SYSTEM`, `BUILTIN\\Administrators`, and the configured owning user SID.
- Remote pipe clients are rejected.
- Each connection is authenticated from the kernel-provided client process and token before any request is read.
- Requests carry protocol version, request id, operation enum, and operation-specific payload.
- Responses carry request id, success, stable result code, sanitized diagnostic, and operation-specific evidence.
- Unknown versions, operations, and fields that change security meaning are rejected.
- Requests cannot contain executable paths, command lines, scripts, environment blocks, registry paths, device instance ids, or unrestricted filesystem paths.
- The agent never invokes a shell. Any required platform tool is called directly with a fixed executable and validated argument model until an equivalent SetupAPI implementation replaces it.

The initial operation set is:

- `GetAgentStatus`
- `ValidateDisplayAccess`
- `ActivateVirtualDisplay`
- `SetVerifiedDisplayPrimary`
- `RestoreVerifiedPhysicalPrimary`
- `ApplyExtendedTopology`
- `SetVerifiedAdvancedColorState`
- `GetSudoVdaStatus`
- `StageSudoVdaPackage`
- `InstallStagedSudoVdaPackage`
- `RollbackSudoVdaPackage`

Display requests identify a Windows display source name plus expected kind, resolution, refresh rate, and stable adapter/target evidence. Host Agent re-resolves and verifies that evidence immediately before mutation. It never accepts a caller assertion that an arbitrary display is physical or virtual.

## Driver Package Staging

Driver updates use `%ProgramData%\\Beacon\\HostAgent` with separate `Inbox`, `Staged`, `InstalledEvidence`, and `Logs` directories.

1. Beacon Service writes a package under `Inbox\\<package-id>` using its granted create-only/update ACL.
2. The request names only `<package-id>`; Host Agent resolves the canonical path under `Inbox`.
3. Host Agent rejects reparse points, alternate data streams, absolute manifest paths, parent traversal, unexpected files, and files outside the package root.
4. A versioned manifest lists every relative file, SHA-256 digest, package version, expected SudoVDA hardware id, protocol version, architecture, and signer identity.
5. Host Agent verifies manifest structure, all hashes, catalog signature, signer allowlist, INF identity, architecture, and protocol compatibility before copying the package into its protected staged directory.
6. Production packages require a trusted production signer. Development packages require the explicitly installed Beacon development certificate and cannot silently weaken production policy.

Dropping a DLL alone is never an update request. A SudoVDA update is a complete INF/CAT/binary package with a verified manifest.

## Update Transaction

Host Agent accepts an install request only when Beacon Service reports no active lease and Host Agent independently confirms there is no active Beacon/SudoVDA display target. A driver update cannot race an active stream or prepared desktop.

The transaction is:

1. Record current published INF, active device instance, driver version, protocol version, signer, and active binary hash.
2. Verify the staged package again from protected storage.
3. Add and install the package using fixed Windows driver-install operations.
4. Restart only the verified SudoVDA device instance.
5. Reopen the driver and verify device state, published INF, protocol version, version, signer, and active binary hash.
6. Publish durable success evidence before returning success.
7. If post-install verification fails, reinstall the recorded prior package, restart the same verified device, and verify rollback.
8. If rollback cannot be verified, return a distinct degraded result with exact evidence. Do not retry indefinitely, restart unrelated devices, or claim success.

Client disconnection does not cancel an in-flight driver transaction. The operation reaches a deterministic result and writes durable evidence; a reconnect can query the result by request id.

## Display Execution

Beacon Service computes the requested topology and verifies the final topology. Host Agent performs only the required privileged mutation. Every request and response includes before/after topology evidence sufficient for Beacon Service to reject stale or incorrect results.

Display mutation rules:

- Preserve at least one verified physical path during virtual-display activation.
- Never enable mirror mode.
- Never substitute the physical display for a missing planned virtual target.
- Reject stale adapter/target evidence instead of mutating a newly changed topology.
- Keep primary changes and lease removal separate.
- Use the valid Windows topology flag combinations covered by Beacon tests.
- Return Windows result codes without translating failure into fabricated success.

## Failure And Recovery

- Host Agent startup failure makes privileged host operations unavailable but does not start or stop sessions by itself.
- Pipe loss leaves existing leases and applications unchanged.
- Beacon Service reconnects and reconciles observed state against desired state.
- No product timeout removes a lease, aborts an update, or restores a display.
- Heartbeats may report agent liveness but cannot own cancellation or cleanup.
- Host Agent logs contain operation ids and sanitized platform evidence, never credentials, stream tickets, media keys, or arbitrary caller payloads.
- Manual Cockpit recovery still flows through Beacon Service policy and then Host Agent mechanics.

## Testing

Static and deterministic tests cover:

- Contract versioning and rejection of unknown operations.
- Pipe ACL construction and remote-client rejection.
- Caller SID/process validation.
- Canonical package confinement, traversal, reparse-point, unexpected-file, hash, manifest, signer, architecture, and INF validation.
- Transaction ordering, post-install verification, rollback, degraded rollback failure, and durable operation lookup.
- Display target revalidation, physical-path preservation, mirror rejection, stale-topology rejection, and exact Windows result propagation.
- Pipe disconnect behavior proving that no lease or session cleanup is inferred.
- Architecture tests proving Core, StreamWorker, StreamCore, and the public client protocol do not reference Host Agent implementation types.

Dynamic validation is staged:

1. Run Host Agent with fake platform executors and a real restricted pipe under the normal test runner.
2. Install the signed Host Agent task once and verify its principal, interactive logon type, highest run level, executable path, no-console behavior, and pipe ACL.
3. From a standard Beacon Service process, request one `2560x1600@120` virtual desktop and verify persistence through the observed GPU topology switch.
4. Remove the desktop under the inactive-client **AND** no-owned-work rule and verify physical-primary restoration.
5. Start StreamWorker only after the display checkpoint passes.
6. Start the Android emulator only after Worker capture binding passes.
7. Validate a staged SudoVDA package update with a non-production test package, then verify active INF, protocol, hash, and rollback evidence.

## Acceptance Criteria

- One explicit installation elevation registers Host Agent for the owning user.
- Routine Beacon startup and approved driver updates require no additional UAC prompt.
- Beacon Service can execute verified display mutations through the local pipe while remaining non-elevated.
- Host Agent exposes no network endpoint and accepts no arbitrary command, path, script, registry, or device operation.
- StreamWorker and the APK have no Host Agent dependency.
- Driver update success is reported only after active-package and protocol verification.
- Failed updates either prove rollback or report a durable degraded state.
- Pipe loss never tears down a display or application.
- Apollo, Sunshine, GameStream, Moonlight, and their lifecycle state are absent from this boundary.
