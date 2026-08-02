# Beacon Release Outcome Gates Implementation Plan

> **For agentic workers:** Execute one release checkpoint at a time. Keep one active blocker,
> prove the next observable product state, commit and push that evidence, and stop at each release
> boundary. Do not expand the active outcome with deferred capabilities.

**Goal:** Deliver a functional Beacon release by first proving one complete minimal streaming
transaction, then adding only playability and distribution requirements.

**Architecture:** Preserve the authoritative Beacon-owned Service, HostAgent, StreamWorker,
StreamCore, SudoVDA, and thin-client boundaries. Change the execution model, not the product model:
the existing production acceptance transaction is the progress meter and the first failed boundary
is the only active implementation target.

**Technology:** .NET 10, ASP.NET Core, WPF, C++20, WGC, D3D11, NVENC, MsQuic, Android Kotlin/Java,
JNI, MediaCodec, SudoVDA, PowerShell, Gradle, CMake, GitHub Actions.

**Execution authority:**
`../specs/2026-08-01-beacon-80-20-release-execution-design.md`

---

## Plan Rules

- This is the sole active implementation plan.
- Complete R1 before planning or implementing R2 internals.
- Complete R2 before implementing R3 packaging.
- Keep one active blocker in `docs/validation/beacon-release-blocker.md` while implementation is in
  progress.
- The blocker ledger is a diagnostic pointer, not a diary. Replace its evidence and next proof when
  the failing boundary changes.
- Do not create sub-milestones for local defects.
- Do not use arbitrary sleeps or cancellation timeouts. Synchronize on observable system state.
- Commit and push each checkpoint that advances the production transaction.
- Do not describe R1 as a release or R2 as a packaged release.

## R1: Integrated Streaming Proof

### Checkpoint 1: Establish The Reproducible Baseline

**Read first:**

- `docs/validation/2026-07-14-production-video-pipeline.md`
- the newest `artifacts/gate5-production-*` failure snapshot and runner log;
- `tests/Beacon.ProductionAcceptance/Program.cs`;
- `scripts/test-gate5-production-session.ps1`; and
- current HostAgent deployment state and source revision.

**Actions:**

1. Fetch the branch, verify the worktree, and identify the installed Server, HostAgent,
   StreamWorker, helper, APK, and driver revisions.
2. Create `docs/validation/beacon-release-blocker.md` with `R1`, the last completed checkpoint, the
   first current failure, one evidence-backed hypothesis, and the next proof.
3. Run the existing production acceptance runner only far enough to reproduce its first failing
   boundary. Preserve its generated snapshot, event stream, and component logs.
4. Confirm that the failure belongs to the current binaries before editing source.

**Exit:** The first production failure is reproducible, tied to exact deployed revisions, and
recorded with one falsifiable next proof.

**Do not:** redesign the harness, add another simulator, broaden the feature set, or run the full
repository matrix.

### Checkpoint 2: Prove Stable Display Preparation

**Primary boundary:**

- `src/Beacon.Platform.Windows/Displays/WindowsShellExtendedTopologyActivator.cs`
- `src/Beacon.Platform.Windows/Displays/WindowsUserDisplayTopologyTransition.cs`
- the interactive user-session helper launch boundary;
- HostAgent display commands and matching focused tests.

**Actions:**

1. Start from verified physical-primary topology.
2. Use the production acceptance runner's existing pre-connect stage to request the client display.
3. Prove through driver heartbeats and DisplayConfig generations that the intended virtual display
   exists at the R1 fixture profile's `2560x1600` mode, is extended, is the stream target, and
   remains the same display across the preparation transaction. This expectation belongs to the R1
   client fixture and must not become a global virtual-display default.
4. If preparation fails, change only the first demonstrated ownership, privilege, or synchronization
   boundary. Add a focused regression before the fix.
5. Repeat the staged preparation and verify that no Apollo, Sunshine, mirror, or physical-capture
   route participates.

**Exit:** The production runner advances beyond virtual-display preparation without manual UAC,
manual topology repair, mirror mode, or a changed virtual-display identity.

**Do not:** add a second display implementation, generalize helper deployment, add display policy
settings, or tune later media stages.

### Checkpoint 3: Complete The Existing Production Transaction

**Command:**

```powershell
.\scripts\test-gate5-production-session.ps1 -Serial emulator-5554 -ArtifactsReady
```

**Method:**

1. Run the command until the first failing transaction boundary.
2. Update the blocker ledger with the exact last successful checkpoint and first failure.
3. Add the narrowest regression that reproduces that boundary.
4. Correct that boundary without adding a fallback route.
5. Run focused static validation for the changed component, then rerun the production transaction.
6. Repeat only this loop until the runner completes.

**Required transaction evidence:**

- the planned virtual display is the capture source;
- the catalog SessionProbe application is shown on that display;
- WGC reports changing frames;
- the APK renders the required moving H.264 SDR frames;
- the authenticated F12 input proof succeeds;
- active disconnect preserves the application and display lease;
- reconnect succeeds with a fresh ticket;
- inactive quit closes the deterministic application;
- physical-primary topology is restored and the unused lease is removed; and
- the runner emits `BEACON_GATE5_PRODUCTION_SESSION_OK`.

**Exit:** One retained artifact directory proves the complete transaction from a clean physical
baseline to a verified physical restore.

**Do not:** add audio, controller emulation, HDR, another codec, phone-specific tuning, stress tests,
or broad refactoring.

### Checkpoint 4: Close R1

Run the relevant complete validation matrix once after Checkpoint 3 passes:

```powershell
dotnet restore Beacon.slnx
dotnet format Beacon.slnx --verify-no-changes --no-restore
dotnet build Beacon.slnx -warnaserror --no-restore
dotnet test Beacon.slnx --no-build
.\scripts\build-native-windows.ps1
gradle -p src\Beacon.Android test assembleDebug
pnpm --dir src\Beacon.ClientLab install --frozen-lockfile
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
```

Also run the repository's architecture/prohibited-route checks and require all PR checks to pass.
If a matrix failure is unrelated to the accepted transaction, fix only the regression required to
make the branch releasable; do not broaden R1.

Update:

- `docs/validation/beacon-release-blocker.md` to record R1 complete and link the retained evidence;
- `README.md` with the observed R1 status; and
- the pull request summary with outcome evidence rather than commit counts.

**Exit:** R1 transaction and static validation are green, evidence is committed, and the integration
branch is synchronized. Stop before R2 implementation.

## R2: Playable Personal Build

R2 receives a focused implementation plan only after R1 is complete. That plan may contain only
these vertical slices:

1. **Audio:** one Windows capture source, one Opus path, and one Android playback sink synchronized
   with the existing session.
2. **Controller:** one Android controller input path terminating in one Windows virtual-controller
   sink through the existing authenticated input contract.
3. **Physical benchmark:** automatic network/hardware change detection and manual benchmark on the
   Z Fold 7, producing a server-owned executable plan. Add the generic per-client mode selector at
   this boundary: exact reported client mode when supported, otherwise the closest same-aspect
   supported mode, with no independent width/height clamping.
4. **Playable transaction:** catalog launch, moving video, audio, controller input, reconnect, quit,
   and physical restore during one sustained gameplay session.

**R2 exit marker:** retained physical-device evidence shows one complete playable transaction and
no unresolved display or process ownership.

**Deferred:** HDR, HEVC, AV1, 120 FPS qualification, multitouch refinement, multi-client concurrency,
UI polish, and packaging.

## R3: Functional Prerelease

R3 receives a packaging plan only after R2 is complete. It may contain only:

1. one unified Windows install/start/update/rollback/uninstall path;
2. unattended privileged deployment through the existing signed administrative boundary;
3. one matching APK artifact;
4. clean-machine execution of the R2 transaction;
5. one evidence-driven refactor of the proven path;
6. the same static and dynamic acceptance before and after that refactor; and
7. publication of a GitHub prerelease with exact artifacts and known limitations.

**R3 exit marker:** the published artifacts install on a clean Windows environment and complete the
playable transaction without Apollo, Sunshine, manual source deployment, or manual topology repair.

**Deferred beyond the prerelease:** HDR, additional codecs, generalized plugins, internet relay,
multi-user service, broad client customization, and feature parity with upstream projects.

## Status Reporting Template

Use this format while executing the plan:

```text
Release outcome: R1 / R2 / R3
Last verified checkpoint: <observable user journey state>
Current blocker: <one failing boundary>
Evidence: <artifact, log, event, or test>
Next proof: <one falsifiable action>
Deferred: <capabilities explicitly outside this outcome>
```

Do not use task counts, commit totals, or percentage complete as the primary status.
