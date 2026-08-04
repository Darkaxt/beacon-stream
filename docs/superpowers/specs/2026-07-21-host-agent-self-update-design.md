# Beacon Host Agent Self-Update Design

Status: approved architecture extension, 2026-07-21

## Purpose

Beacon development must not stop whenever an elevated Host Agent binary changes. After one
final bootstrap installation, Codex must be able to build, authorize, stage, activate, verify,
and, when necessary, roll back a Host Agent update without a person accepting another UAC
prompt.

This is not an arbitrary elevated file-copy or command-execution API. A process running as the
owning Windows user can write the update inbox and request an update, but only a package signed
by Beacon's pinned CI update key can cross the elevated boundary.

## Decision

Use a stable elevated bootstrap as the Task Scheduler executable. The bootstrap launches the
currently selected versioned `Beacon.HostAgent` child and remains its parent. Host Agent stages
signed updates and exits with a dedicated update code only after its response has been written
to the requesting pipe. The bootstrap then independently verifies and activates the candidate.

The signing private key exists only as a GitHub Actions repository secret. The corresponding
ECDSA P-256 public key is committed and compiled into the stable bootstrap. Local unsigned
builds are never eligible for unattended elevation.

## Alternatives Considered

### Mutable Helper Beside Host Agent

Host Agent could spawn a helper and exit so the helper can replace its files. This leaves the
helper itself locked and mutable, complicates rollback, and makes it easy for package contents
to overwrite the authority performing verification. Rejected.

### Re-run The Elevated Installer

The current installer is simple and remains the recovery path, but every code iteration needs
secure-desktop interaction. It does not meet unattended development requirements. Rejected as
the routine path.

### Stable Bootstrap With Versioned Children

The bootstrap is installed once, never included in an ordinary Host Agent update, and contains
the final signature and state-transition authority. Candidate code cannot overwrite it. This
provides a small immutable trust root, event-driven restart, and deterministic rollback.
Selected.

## Trust Boundaries

- The owning user may write `%ProgramData%\Beacon\HostAgent\Inbox` and call the owner-bound
  Host Agent pipe.
- The owning user cannot write protected staged packages, version directories, transaction
  journals, the current-version pointer, or the bootstrap installation.
- The package signing private key is never stored in the repository, Windows user profile,
  build artifact, package, log, or Host Agent installation.
- GitHub Actions receives the private key from
  `BEACON_HOST_AGENT_UPDATE_SIGNING_KEY_PEM_B64`, signs one exact manifest, and uploads the
  package artifact.
- The bootstrap embeds the public key and revalidates the raw manifest signature, package
  shape, and every payload hash immediately before activation.
- A signed Host Agent package can replace only the versioned Host Agent child. It cannot
  replace the bootstrap, scheduled-task definition, signing key, ACL policy, or another
  executable path.

Compromise of the GitHub signing secret authorizes Host Agent code execution as administrator
and therefore requires key removal and a new UAC bootstrap with a rotated public key.

## Installed Process Model

The final scheduled task action is:

```text
C:\Program Files\BeaconStream\Bootstrap\Beacon.HostAgent.Bootstrap.exe
  --owner-sid <SID>
```

The bootstrap:

1. verifies it is elevated, belongs to the configured interactive user, and is not in session
   zero;
2. reads the protected current-version pointer;
3. starts the selected `Beacon.HostAgent.exe` as an elevated child in the same session;
4. creates a one-use readiness named pipe and verifies the connecting pipe client process id
   equals the exact child process id;
5. waits for readiness or child process exit using process and pipe events, without a timeout;
6. remains alive while Host Agent runs; and
7. handles the dedicated update exit code before launching another child.

Host Agent signals readiness only after identity, elevation, mutex, storage, display boundary,
driver-update boundary, dispatcher, and owner-only pipe listener initialization succeed.

## Protected Layout

```text
C:\Program Files\BeaconStream\Bootstrap\
  Beacon.HostAgent.Bootstrap.exe

C:\Program Files\BeaconStream\HostAgent\Versions\
  <package-id>\
    Beacon.HostAgent.exe
    Beacon.HostAgent.dll
    ... exact signed publish payload ...

C:\ProgramData\Beacon\HostAgent\
  Inbox\<package-id>\                 owning user may modify
  StagedHostAgent\<transaction-id>\   administrators and SYSTEM only
  HostAgentTransactions\              administrators and SYSTEM write; owner read
  State\current-version.json          administrators and SYSTEM only
  State\pending-update.json           administrators and SYSTEM only
```

Version and state directories inherit protected ACLs from installer-owned parents. Package
enumeration rejects reparse points and alternate data streams before reading content.

## Signed Package Contract

Each package contains exactly:

```text
manifest.json
manifest.sig
payload/<declared files>
```

`manifest.json` is emitted deterministically and contains:

- schema version;
- package id;
- source commit SHA;
- target `beacon-host-agent`;
- architecture `win-x64`;
- minimum bootstrap version;
- entry point `Beacon.HostAgent.exe`; and
- every normalized relative payload path, byte length, and SHA-256 digest.

`manifest.sig` is an ECDSA P-256/SHA-256 signature over the exact `manifest.json` bytes. The
validator rejects unknown fields, invalid ids, rooted paths, parent traversal, duplicate paths,
case-colliding paths, empty segments, unexpected files, missing files, reparse points,
alternate data streams, length mismatch, hash mismatch, wrong target or architecture, a newer
minimum bootstrap version, an undeclared entry point, and an invalid signature.

The bootstrap validates from its embedded public key. Candidate Host Agent validation is an
early diagnostic only and cannot replace bootstrap authorization.

## Update Transaction

The typed pipe operations are:

- `InstallStagedHostAgentPackage(packageId, transactionId)`
- `QueryHostAgentUpdate(transactionId)`

The durable states are `accepted`, `validating`, `staged`, `installing`,
`awaitingReadiness`, `succeeded`, `rolledBack`, and `degraded`.

The sequence is:

1. Host Agent validates the inbox package and copies it into a protected transaction staging
   directory.
2. Host Agent validates the protected copy again, writes `staged`, and persists
   `pending-update.json` with the transaction id, package id, and previous version id.
3. Host Agent returns the accepted/staged transaction response through the pipe.
4. Only after the response frame is flushed, Host Agent stops its pipe loop and exits with the
   dedicated update code. Pipe loss before this point does not request an update.
5. Bootstrap reads the pending transaction, validates it independently, copies payload into a
   fresh `<package-id>.pending` version directory, verifies the copy, and atomically renames it
   to `<package-id>`.
6. Bootstrap durably writes `installing`, atomically switches the current-version pointer,
   starts the candidate, and writes `awaitingReadiness`.
7. Candidate readiness writes `succeeded`, clears pending state, and leaves the bootstrap
   supervising that child.
8. Candidate exit before readiness restores the previous pointer and starts the previous
   version exactly once.
9. Verified previous-version readiness writes `rolledBack`. Failure of the previous version
   before readiness writes `degraded` and exits nonzero.

No timer, sleep, startup delay, or cancellation timeout owns this transaction. Child process
exit and authenticated readiness are the only startup outcomes.

## Build And Deployment Pipeline

The GitHub workflow is manually dispatchable for any branch in this repository. It restores
and tests the Host Agent projects, publishes `win-x64`, invokes the deterministic package
builder with the private signing key, verifies the completed package with the public key, and
uploads one artifact named with the request id and source commit.

The local update script performs one unattended command flow:

1. require a clean committed source revision and an authenticated `gh` session;
2. dispatch the workflow for the current branch with a cryptographically random request id;
3. observe workflow creation and completion as monitoring heartbeats, without a cancellation
   timeout;
4. download the matching artifact;
5. stage the package under the ACL-controlled inbox;
6. submit the typed Host Agent update request;
7. reconnect after bootstrap restart and query the durable transaction; and
8. verify transaction success, installed package id, process ancestry, scheduled-task action,
   installed payload hashes, and Host Agent readiness.

The script fails rather than falling back to UAC, an unsigned local build, an arbitrary path,
or direct writes into protected installation directories.

## Recovery And Retention

- The elevated installer remains the explicit recovery mechanism for a damaged bootstrap,
  signing-key rotation, or unrecoverable state.
- Bootstrap keeps the current version and one previously successful version with transaction
  context. Older version directories are removed only after a newer candidate reaches verified
  readiness.
- A version directory is never treated as a generic backup. Its manifest, transaction, source
  commit, and activation result provide the recovery context.
- Unknown pending state, pointer mismatch, signature failure, or protected-copy mismatch fails
  closed and preserves the last selected version.
- Bootstrap never launches a package directly from the user-writable inbox.

## Testing

Deterministic tests prove:

- manifest signature, structure, exact file set, confinement, stream, reparse, length, hash,
  target, architecture, minimum-version, and entry-point validation;
- updater pipe operations and durable idempotent transaction lookup;
- response flush precedes the update exit request;
- normal child readiness, candidate activation, atomic pointer switch, and success journal;
- candidate exit before readiness, single rollback, previous readiness, and rolled-back journal;
- previous-version startup failure and degraded journal;
- readiness rejection when the named-pipe client pid is not the launched child;
- no timeout, shell command, arbitrary executable path, or bootstrap replacement surface;
- workflow packaging followed by independent verification; and
- architecture boundaries remain absent from Core, StreamWorker, StreamCore, and public client
  contracts.

Dynamic acceptance requires one final UAC bootstrap installation, followed by a CI-signed
same-version update through the unattended pipeline. Evidence must prove no new consent
process, the task still targets the stable bootstrap, Bootstrap is the Agent parent, the
transaction is `succeeded`, installed hashes match the signed manifest, and Host Agent accepts
a normal status request after restart.

## Non-Goals

- Updating the stable bootstrap without UAC.
- Accepting unsigned local development builds.
- Automatic background update discovery or scheduled updates.
- Remote administration or a network update endpoint.
- Updating SudoVDA, StreamWorker, Server, Cockpit, or the APK through this package type.
- Restarting an active stream, changing display topology, or killing applications to make an
  update proceed.

## Acceptance Criteria

- One final UAC installation deploys the bootstrap trust root and migrates the scheduled task.
- Every subsequent signed Host Agent update can be built, staged, installed, verified, and
  queried by Codex without a person at the laptop.
- A Windows user process cannot convert an unsigned package or arbitrary file path into
  elevated execution.
- Bootstrap independently authorizes every candidate and cannot be overwritten by a Host Agent
  package.
- Candidate startup either reaches authenticated readiness or deterministically restores the
  previous verified version.
- Update state remains durable across Agent and Service disconnects.
- No timeout owns update cancellation, startup acceptance, rollback, or cleanup.
- Routine display and driver operations remain unchanged and no Apollo-compatible path is
  introduced.
