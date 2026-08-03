# Personal Streaming Orchestrator Design

Status: authoritative architecture-recovery revision, 2026-08-04

## Purpose

Beacon Stream is a personal Windows game-streaming system with one server-owned product architecture and one thin Android client. Beacon replaces Apollo/Sunshine as the installed server product. It may reuse carefully extracted implementation primitives from Sunshine, Apollo, their forks, Moonlight, and related projects when that is the strongest engineering choice, but it does not preserve compatibility with their control planes, settings, pairing, application model, or streaming protocols.

The Windows server knows registered clients before launch, measures their current network and hardware behavior, computes one complete session plan, prepares the correct virtual desktop, launches the selected Windows application, and owns the stream until verified cleanup. The Android APK identifies itself, benchmarks the current endpoint, selects an application from the server catalog, presents the stream, forwards input, and exposes only local interaction settings.

This document replaces compatibility-first assumptions introduced during the first 120 pull requests. Where an older plan, README statement, test, or implementation contradicts this document, this document wins.

## Execution Authority

The architecture in this document remains authoritative. Delivery priority, progress reporting,
and release claims are governed by
`2026-08-04-beacon-core-hardening-execution-design.md` and
`../plans/2026-08-04-beacon-core-hardening-outcome-gates.md`.

The release sequence is:

1. The completed R1 H.264 SDR transaction remains useful integration evidence, but it is not core
   completion.
2. Core Stage 1 selects and hardens one virtual-display driver boundary, beginning with a decisive
   HDR-capability feasibility probe and ending with durable recovery validation.
3. Core Stage 2 implements and validates the complete HDR chain from the selected virtual display
   through physical Android presentation.
4. Only after Core Stages 1 and 2 pass may delivery resume on the playable-client, remaining input,
   remaining codec, packaging, and prerelease outcomes.
5. If no viable HDR path can be proven, execution stops with a concrete boundary report and requires
   a product decision. SDR fallback is valid session behavior but is not an HDR acceptance result.

Older unchecked milestone items are historical evidence, not parallel requirements. This execution
reset does not weaken Beacon's architecture, security, server-owned policy, virtual-display
lifecycle, HDR truthfulness, physical-display integrity, or inactive **AND** no-owned-work
invariants.

## Core Thesis

The product must not accumulate layers around Apollo, Sunshine, GameStream, Moonlight, wrappers, runtime descriptor files, or alternate Android routes. Those approaches duplicate ownership and retain failure modes that Beacon exists to remove.

The intended lifecycle is:

```text
APK identifies itself
-> Beacon loads server-owned client policy
-> APK runs required network and hardware benchmark phases
-> Beacon computes one executable session plan
-> Beacon prepares and verifies the client's virtual desktop
-> Beacon starts its bundled StreamWorker with that immutable plan
-> Beacon launches the selected Windows application on the leased display
-> APK connects to StreamWorker using one Beacon session ticket
-> StreamWorker captures, encodes, transports, and accepts input
-> Beacon stops and restores the session through verified compensating actions
```

The server owns desired and executable state. The APK reports facts, benchmark measurements, local user intent, and input. The APK does not negotiate or reinterpret stream policy.

## Project Identity And Repository

- Product name: **Beacon Stream**, shortened to **Beacon** in UI and service names.
- Public repository: `Darkaxt/beacon-stream`.
- Beacon is not an Apollo, Sunshine, Artemis, or Moonlight fork as a product.
- The repository remains GPL-3.0 compatible because upstream GPL source may be adapted.
- Every copied or adapted source boundary must be recorded with origin, revision, license, retained behavior, removed behavior, and local ownership.
- Upstream projects are implementation evidence and source material, not runtime dependencies or architecture authorities.

## Architecture Invariants

These invariants are non-negotiable.

1. Beacon exposes one production control plane and one production streaming path.
2. Apollo, Sunshine, Moonlight, Artemis, Vibepollo, and Vibeshine compatibility is not a product requirement.
3. No separately installed upstream server is required for Beacon to stream.
4. Beacon Core contains no upstream-protocol-specific session types.
5. The APK contains one streaming implementation, not native, Java, fallback, wrapper, and intent routes in parallel.
6. The production host data plane is a bundled Beacon component controlled through a versioned private contract.
7. Fake implementations exist only for deterministic tests and use the same Beacon-owned contracts.
8. Display, application, session, and recovery ownership remain in Beacon Service, never in StreamWorker or the APK.
9. Benchmark measurements are facts. Only Beacon Service selects streaming settings.
10. Secondary feature work remains frozen until the selected virtual-display driver and the complete HDR path pass their core acceptance gates.
11. Privileged Windows execution is isolated in one Beacon Host Agent that implements typed mechanics without owning session policy.

## Product Components

### Beacon Service

Beacon Service is the authoritative Windows host and control plane. It runs non-elevated as the owning user in the interactive Windows session; privileged mechanics are delegated to Beacon Host Agent without transferring policy ownership.

Responsibilities:

- Registered client identity and credentials.
- Server-owned client policies.
- Network and hardware benchmark orchestration and storage.
- Session planning.
- Per-client virtual-display creation, activation, persistence, cleanup, and recovery.
- Game and Windows application discovery, artwork, selection, and launch.
- Process and window ownership.
- StreamWorker lifecycle and private IPC.
- Session tickets and public client API.
- Operational journal, health, diagnostics, and recovery.

Beacon Service does not capture or encode frames in its managed service process. Native streaming failures must not crash or corrupt the policy owner.

### Beacon Host Agent

Beacon Host Agent is the sole elevated Beacon process. It runs in the owning user's interactive Windows session, exposes no network endpoint, and executes a fixed versioned set of display and driver operations over a SID-restricted named pipe. Beacon Service remains the policy and lifecycle owner; Host Agent cannot plan sessions, infer cleanup, launch applications, or start StreamWorker.

The normative process, IPC, package-verification, transaction, and validation requirements are defined in `2026-07-21-beacon-host-agent-design.md`.

### Beacon StreamWorker

StreamWorker is a bundled, headless, policy-free native worker. It is part of Beacon, not a user-configured external backend.

Responsibilities:

- Capture the display selected by the immutable session plan.
- Convert and encode video using the selected GPU path.
- Capture and encode the selected audio mix.
- Run the single Beacon streaming transport.
- Receive and validate client input for the active session.
- Publish structured readiness, measurements, state transitions, and failures to Beacon Service.
- Stop deterministically when Beacon closes the session or the private IPC channel ends.

StreamWorker must not:

- Discover or launch games.
- Create, remove, make primary, mirror, or restore displays.
- Read client profiles or global settings.
- Select codecs, resolution, FPS, bitrate, HDR, or recovery policy.
- Expose an Apollo/Sunshine web UI, app list, pairing endpoint, or configuration file.
- Poll descriptor files or supervise another streaming server.

### Beacon StreamCore

StreamCore is the single native data-plane library packaged in the APK.

Responsibilities:

- Authenticate the Beacon session ticket.
- Receive, decrypt, reorder, recover, and decode Beacon media.
- Present video and audio.
- Forward input through the active Beacon session.
- Report stream measurements and decoder state.
- Stop and release all native resources deterministically.

StreamCore may adapt proven upstream algorithms or implementation primitives, but its public API and wire contract are Beacon-owned. It must not expose upstream discovery, pairing, app launch, settings, or compatibility APIs.

### Beacon Android APK

The APK is a thin, policy-free client around StreamCore.

User-visible responsibilities:

- Register and authenticate with Beacon.
- Show connection and benchmark state.
- Run automatic and manual network/hardware benchmarks.
- Show the server-owned Windows application/game catalog.
- Select and launch one entry.
- Present the active stream.
- Forward touch, keyboard, mouse, controller, and Android-device motion input supported by the device.
- Stop the session.
- Request owning-session emergency recovery.
- Edit local-only interaction and presentation settings.

The APK has no streaming settings page. It cannot choose or edit resolution, refresh rate, FPS, bitrate, codec, HDR, audio mode, transport, display mode, virtual-display lifecycle, recovery policy, or server-global settings.

Allowed local settings include:

- Touch layout and gesture mapping.
- Controller overlay and button mapping.
- Device-motion enablement, calibration, sensitivity, and axis inversion.
- Haptics.
- Local UI density and theme.
- Wake lock behavior.
- Decoder diagnostics overlay visibility.
- Accessibility behavior that does not change server stream policy.

### Beacon Cockpit

The WPF cockpit is the local administrative UI for Beacon Service.

Responsibilities:

- Registered clients and server-owned policies.
- Benchmark history and planner decisions.
- Active sessions, displays, applications, and StreamWorker state.
- Game and application catalog.
- Global streaming limits and server capabilities.
- Recovery actions.
- Logs and diagnostics.

The cockpit contains no duplicate policy. It calls Beacon Service APIs.

### Client Lab And Fake Endpoint

Client Lab and the CLI fake endpoint remain development tools. They simulate APK control-plane behavior and benchmark reports without creating a second production client protocol.

## Requirement Register

This register is the implementation contract.

### Project And Workflow

- `REQ-PROJ-001`: Beacon is personal-use first and optimizes for one trusted Windows host and known personal clients.
- `REQ-PROJ-002`: Z Fold 7 is the first real endpoint, while identity and policy storage support additional registered clients.
- `REQ-PROJ-003`: Beacon remains a new public repository, not a long-lived upstream fork.
- `REQ-PROJ-004`: Source provenance and GPL obligations must remain explicit for every adapted upstream component.
- `REQ-PROJ-005`: Existing projects are source material only; their product boundaries and compatibility requirements do not carry into Beacon.
- `REQ-PROJ-006`: Documentation, contracts, tests, and code must agree after every synced checkpoint.
- `REQ-SYNC-001`: Work follows implement, static/dynamic validation, sync, refactor, static/dynamic validation, sync.
- `REQ-SYNC-002`: A sync is a coherent commit pushed to GitHub with passing required checks.
- `REQ-SYNC-003`: No substantial validated objective work remains local across context compaction.
- `REQ-SYNC-004`: Architecture recovery uses small deletion or boundary-repair slices, each independently validated before merging.
- `REQ-SYNC-005`: A green test suite does not justify retaining code that violates an architecture invariant.

### Product Boundary

- `REQ-BOUND-001`: Beacon must stream without Apollo or Sunshine installed, running, configured, queried, or controlled.
- `REQ-BOUND-002`: Beacon must not implement Apollo, Sunshine, GameStream, Moonlight, or Artemis compatibility as a product feature.
- `REQ-BOUND-003`: Beacon must not expose upstream pairing, app-list, launch, cancel, RTSP, NVHTTP, runtime-descriptor, or wrapper-manifest contracts.
- `REQ-BOUND-004`: Beacon must ship one production StreamWorker and one APK StreamCore path.
- `REQ-BOUND-005`: The user must not select a streaming backend, compatibility mode, wrapper, or protocol.
- `REQ-BOUND-006`: Diagnostic fake streaming must implement Beacon contracts and must not introduce a second client route.
- `REQ-BOUND-007`: Upstream source may be retained only when it is the best implementation primitive after removing upstream policy and compatibility surfaces.
- `REQ-BOUND-008`: Retained upstream code must be owned by a narrow Beacon adapter or module with explicit tests and provenance.

### Server And Client Ownership

- `REQ-CTRL-001`: Beacon Service is the sole source of truth for desired and executable session state.
- `REQ-CTRL-002`: Beacon computes a complete immutable session plan before display activation, app launch, or StreamWorker start.
- `REQ-CTRL-003`: The APK reports identity, capabilities, benchmark facts, local user intent, stream health, and input.
- `REQ-CTRL-004`: The APK cannot edit server streaming policy or global settings.
- `REQ-CTRL-005`: The APK cannot reinterpret codec, resolution, FPS, bitrate, HDR, audio, transport, display, or recovery decisions.
- `REQ-CTRL-006`: Local interaction settings remain in the APK and never affect the server session plan except by reporting device capability constraints.
- `REQ-CTRL-007`: The WPF cockpit is the only version-one UI for editing server-global and server-owned per-client streaming policy.
- `REQ-CTRL-008`: Anything that affects virtual desktop behavior is server-owned.
- `REQ-CTRL-009`: The APK may select a catalog application and request launch, stop, and owning-session emergency recovery.
- `REQ-CTRL-010`: StreamWorker executes an immutable plan and cannot mutate server policy.

### Client Registration And Security

- `REQ-SEC-001`: Every APK installation has a stable Beacon client identity and server-issued credential.
- `REQ-SEC-002`: Registration requires explicit approval from the trusted Windows host.
- `REQ-SEC-003`: Control-plane traffic and stream setup are authenticated and encrypted.
- `REQ-SEC-004`: Stream tickets are single-use, short-lived, bound to client id, session id, plan revision, and StreamWorker instance.
- `REQ-SEC-005`: Ticket expiry is a security validity rule, not a session cancellation timeout.
- `REQ-SEC-006`: Private keys, credentials, session tickets, and media keys never appear in public API snapshots, logs, exception text, or `ToString()` output.
- `REQ-SEC-007`: Owning-client actions are scoped to that client's active session.
- `REQ-SEC-008`: The local cockpit requests broader administrative recovery through Beacon Service and Host Agent; it does not directly elevate or bypass server policy.

### Privileged Host Boundary

- `REQ-HOST-001`: One installer-time elevation registers `Beacon.HostAgent` for the owning user's interactive logon at highest run level.
- `REQ-HOST-002`: Routine Beacon startup, display control, recovery, and approved SudoVDA updates do not prompt for UAC.
- `REQ-HOST-003`: Host Agent is the only elevated Beacon process and exposes no network listener.
- `REQ-HOST-004`: Beacon Service owns all desired state, ordering, session policy, cleanup gates, and compensation; Host Agent executes typed Windows mechanics only.
- `REQ-HOST-005`: Host Agent IPC is versioned, local-only, bound to the owning user SID, protected by an explicit pipe ACL, and authenticated from the kernel-provided client identity.
- `REQ-HOST-006`: Host Agent accepts no arbitrary command, executable path, command line, script, environment block, registry path, device instance id, or unrestricted filesystem path.
- `REQ-HOST-007`: Pipe disconnect never implies stream stop, display removal, primary restoration, application termination, or any other lifecycle transition.
- `REQ-HOST-008`: Display mutations re-resolve and verify target identity and preserve a physical path immediately before execution.
- `REQ-HOST-009`: Driver updates use complete manifest-bound INF/CAT/binary packages staged below an ACL-controlled Beacon directory; a loose DLL is never installable.
- `REQ-HOST-010`: Host Agent rejects package traversal, reparse points, unexpected files, hash mismatch, signer mismatch, INF mismatch, architecture mismatch, and incompatible driver protocol.
- `REQ-HOST-011`: Driver update success requires verification of the active published INF, device state, protocol, signer, version, and binary hash after device restart.
- `REQ-HOST-012`: Failed driver verification triggers one deterministic rollback to the previously recorded package; unverified rollback is reported as a durable degraded state.
- `REQ-HOST-013`: No timeout cancels a host operation or owns cleanup. Heartbeats report liveness only.
- `REQ-HOST-014`: Core, StreamWorker, StreamCore, and the public APK protocol contain no Host Agent implementation contract.
- `REQ-HOST-015`: One stable elevated bootstrap is the scheduled-task executable and launches the selected versioned Host Agent as its child.
- `REQ-HOST-016`: Unattended Host Agent updates require an exact manifest signed by the CI update key whose public key is pinned in the installed bootstrap; unsigned local builds are rejected.
- `REQ-HOST-017`: Host Agent update packages cannot replace the bootstrap, task definition, signing key, ACL policy, or files outside a fresh version directory.
- `REQ-HOST-018`: Candidate activation occurs only after independent bootstrap validation of signature, package shape, and every payload hash.
- `REQ-HOST-019`: Host Agent returns and flushes the accepted update response before exiting with the dedicated bootstrap update code.
- `REQ-HOST-020`: Bootstrap accepts readiness only from the exact launched child process and waits on readiness or process exit without a startup timeout.
- `REQ-HOST-021`: Candidate exit before readiness causes one deterministic rollback to the previous verified version; failed previous-version readiness is durably degraded.
- `REQ-HOST-022`: Update transaction state, selected version, pending candidate, source commit, manifests, and activation evidence are durable and queryable after restart.
- `REQ-HOST-023`: Codex can dispatch, download, stage, request, and verify a CI-signed Host Agent update without interactive UAC after the final bootstrap installation.
- `REQ-HOST-024`: Updating the stable bootstrap or rotating its pinned update key remains an explicit UAC recovery operation.

### Network Fingerprint And Benchmark Triggers

- `REQ-BENCH-001`: The APK provides automatic and manual network and hardware benchmarks.
- `REQ-BENCH-002`: Beacon defines the versioned benchmark suite and interprets all results.
- `REQ-BENCH-003`: The APK reports raw measurements and capability evidence; it does not recommend or select streaming settings.
- `REQ-BENCH-004`: The APK listens to platform network-change callbacks instead of polling on a timer.
- `REQ-BENCH-005`: A network fingerprint includes every available non-secret discriminator needed to distinguish materially different paths: transport type, server route/address, local network prefix, Wi-Fi band/channel, link-speed bucket, and a locally salted hash of SSID/BSSID when Android permits access.
- `REQ-BENCH-006`: Raw SSID and BSSID values must not leave the APK or appear in logs.
- `REQ-BENCH-007`: A full automatic benchmark runs when the network fingerprint, device capability revision, Android version, APK version, display-mode inventory, codec inventory, or benchmark schema changes.
- `REQ-BENCH-008`: Every launch runs a lightweight session preflight so congestion changes on the same network are measured.
- `REQ-BENCH-009`: A manual Benchmark action always permits a new full run.
- `REQ-BENCH-010`: Benchmark state and results are stored server-side by client, network fingerprint, hardware revision, benchmark schema, and execution timestamp.
- `REQ-BENCH-011`: Automatic benchmark triggers are event-driven and must not use a periodic watchdog.
- `REQ-BENCH-012`: The full benchmark tests every codec mutually supported by the server encoder and client decoder, then the server ranks the sustainable H.264, HEVC, and AV1 candidates using measured decode, encode, network, latency, thermal, and power evidence.
- `REQ-BENCH-013`: The persisted benchmark result contains the selected codec, ranked qualified fallbacks, rejected candidates, and a concrete reason for every selection or rejection. The APK reports measurements but does not make this decision.
- `REQ-BENCH-014`: The lightweight session preflight confirms that the selected benchmark result remains viable under current network congestion and server load. It may move only to another benchmark-qualified candidate and records the reason; it does not guess an untested codec.

### Network Benchmark

- `REQ-NET-001`: The benchmark measures round-trip latency, jitter, downstream packet loss, control-path upstream behavior, sustainable downstream throughput, burst handling, and reordering.
- `REQ-NET-002`: Benchmark traffic uses the same Beacon transport implementation and encryption path as production streaming.
- `REQ-NET-003`: A benchmark round uses explicit packet counts, byte counts, sequence numbers, and a versioned measurement interval.
- `REQ-NET-004`: A measurement interval ending records missing packets as loss; it does not cancel or crash the benchmark process.
- `REQ-NET-005`: User cancellation and connection loss end a benchmark with an explicit incomplete result rather than a fabricated recommendation.
- `REQ-NET-006`: Beacon retains raw measurements and the planner decision derived from them.
- `REQ-NET-007`: The planner must distinguish measured sustainable throughput from transient link speed advertised by Android.
- `REQ-NET-008`: The session plan must explain bitrate, FPS, transport-recovery, and latency selections using current measurements.

### Hardware Benchmark

- `REQ-HW-001`: Passive capability inventory records decoder codec, profile, level, bit depth, maximum advertised size/rate, low-latency evidence, display modes, HDR types, and audio output capabilities.
- `REQ-HW-002`: Active decoder tests use versioned Beacon-provided media vectors and the production StreamCore decode/presentation path.
- `REQ-HW-003`: Active tests measure successful configuration, sustained decoded FPS, decode latency, presentation latency when observable, dropped frames, output errors, and thermal-state change.
- `REQ-HW-004`: Candidate tests cover H.264, HEVC, and AV1 only where the device advertises the required decoder profile.
- `REQ-HW-005`: Candidate tests cover 8-bit and 10-bit paths separately.
- `REQ-HW-006`: HDR capability requires decoder evidence, 10-bit vector success, Android display HDR evidence, and successful HDR presentation mode activation.
- `REQ-HW-007`: Full calibration includes sustained workloads long enough to expose thermal throttling and unstable advertised modes.
- `REQ-HW-008`: The planner must reject a mode that the active benchmark cannot sustain even if Android advertises it.
- `REQ-HW-009`: Hardware benchmark failures are facts and must not crash the APK or alter server policy directly.
- `REQ-HW-010`: Emulator benchmarks are valid for protocol and lifecycle testing but cannot certify physical-device HDR, thermal, touch, controller, gyroscope, or radio behavior.

### Session Planning

- `REQ-PLAN-001`: The plan includes client id, app id, display identity, display mode, resolution, refresh rate, stream FPS, codec/profile/bit depth, bitrate, HDR state, audio mode, transport parameters, input capabilities, benchmark evidence revision, and recovery policy.
- `REQ-PLAN-002`: Virtual-display resolution is selected per client from reported display geometry and supported modes; Beacon has no universal `2560x1600` default for every client.
- `REQ-PLAN-003`: The server selects the exact client mode when the complete path supports it, otherwise the closest supported mode with the same aspect ratio before considering a different aspect ratio. It must never clamp width and height independently or silently convert 16:10 intent to 16:9.
- `REQ-PLAN-004`: The Z Fold 7 policy may target `2560x1600` and `120 Hz`; an AYN Thor or another Full HD 16:9 client should normally target `1920x1080`, subject to the modes that client actually reports.
- `REQ-PLAN-005`: Client display geometry and supported modes are hardware facts. The server owns the selected virtual-display mode and persists the resulting per-client policy.
- `REQ-PLAN-006`: Stream resolution and game render resolution are separate concepts; Beacon does not force a game's internal rendering setting.
- `REQ-PLAN-007`: The planner chooses settings from server capabilities, client policy, current benchmark evidence, application constraints, and server load.
- `REQ-PLAN-008`: The plan records a human-readable reason for every downgrade or fallback.
- `REQ-PLAN-009`: If an explicitly required capability cannot be provided, launch fails before display or application side effects.
- `REQ-PLAN-010`: The APK receives the executable plan for display and diagnostics but cannot modify it.
- `REQ-PLAN-011`: The executable plan consumes the latest valid server-interpreted benchmark codec selection for that client and network/hardware fingerprint; it never assumes one codec is universally best or independently guesses a codec at launch.
- `REQ-PLAN-012`: A manual codec preference is server-owned per-client policy. It may constrain the candidates, but launch still fails or falls back according to that policy when the measured path cannot sustain the requested codec.

### Virtual Display Lifecycle

- `REQ-DISP-001`: Virtual-display identity is per client and never shared across clients.
- `REQ-DISP-002`: Active client presence may prepare or verify its leased display without making it primary.
- `REQ-DISP-003`: Launch activates the already-prepared display selected by the plan.
- `REQ-DISP-004`: Launches bind to the owning client's leased display.
- `REQ-DISP-005`: Mirror mode is prohibited unless a future specification explicitly adds it.
- `REQ-DISP-006`: Beacon never silently captures or launches on the physical display when the planned virtual display is unavailable.
- `REQ-DISP-007`: Preflight attempts safe repair before failing display readiness.
- `REQ-DISP-008`: Stream disconnect alone does not imply display teardown.
- `REQ-DISP-009`: Stream stop and display cleanup are separate operations.
- `REQ-DISP-010`: Remove a leased display only when the client is inactive **AND** no owned process, child process, or tracked window remains.
- `REQ-DISP-011`: No timeout replaces the inactive-client **AND** no-owned-work cleanup gate.
- `REQ-DISP-012`: Physical-primary restore is verified and reconciled, not a one-shot best-effort call.
- `REQ-DISP-013`: The laptop panel cannot remain inactive when no session owns that state.
- `REQ-DISP-014`: An owned application may keep the virtual display alive without keeping it primary or stealing the physical desktop.
- `REQ-DISP-015`: Before/after topology, display id, resolution, refresh, primary state, HDR state, and reason are journaled for every topology operation.
- `REQ-DISP-016`: Beacon owns a SudoVDA control session while at least one Beacon display lease exists and sends the driver heartbeat required to preserve those leases.
- `REQ-DISP-017`: The driver heartbeat is a liveness mechanism only. Its schedule or failure cannot remove a lease, trigger cleanup, or replace the inactive-client **AND** no-owned-work cleanup gate.
- `REQ-DISP-018`: Beacon derives heartbeat cadence from the driver-reported watchdog contract, shares one control session across its own concurrent leases, and closes it after the final Beacon lease is released.
- `REQ-DISP-019`: Beacon does not read or write Apollo settings, control the Apollo service or process, reuse Apollo lifecycle state, or require Apollo to keep SudoVDA displays alive.
- `REQ-DISP-020`: Beacon does not rewrite machine-wide SudoVDA watchdog or monitor-capacity configuration. Driver capacity exhaustion and heartbeat failures are reported as explicit readiness or session faults.
- `REQ-DISP-021`: The privileged HostAgent is the single writer of an ACL-protected atomic recovery journal for each owned lease. The journal contains client identity, display GUID, intended mode and topology supplied by Beacon Service, last completed privileged lifecycle phase, and the verified physical baseline needed for compensation; Beacon Service may query but not independently rewrite it.
- `REQ-DISP-022`: HostAgent startup reconciles exact SudoVDA display identities against its recovery journal before accepting a display command, and Beacon Service startup adopts that reconciled inventory before accepting a launch. Beacon may repair, restore, or remove only displays proven to be Beacon-owned; unrelated virtual or physical displays remain untouched.
- `REQ-DISP-023`: A verified physical-only baseline is captured before the first topology mutation and replaced only while no Beacon lease exists and Windows exposes a stable physical-only topology. A missing or corrupt current snapshot falls back to that verified baseline rather than an inferred generic layout.
- `REQ-DISP-024`: Recovery chooses restore-before-remove or remove-before-restore from observed topology and retained baseline evidence, then independently verifies the final physical-primary state. Neither ordering is assumed universally safe.
- `REQ-DISP-025`: A HostAgent-owned production recovery supervisor, not only an acceptance-test guard, observes Windows power resume, session unlock, sign-out, shutdown, Beacon Service loss/restart, and HostAgent restart events. Beacon Service remains the policy owner while available; the supervisor owns privileged compensation and any user-session helper dispatch recorded in the journal.
- `REQ-DISP-026`: Driver disablement, disappearance, protocol mismatch, failed heartbeat, or device restart is diagnosed against the exact expected PnP instance. Safe re-enable or restart is performed only through the privileged HostAgent, is serialized against display and driver-update transactions, and is verified before streaming resumes.
- `REQ-DISP-027`: Recovery does not use a timeout or fixed retry count as lifecycle authority. It advances on driver acknowledgements, PnP state changes, Windows topology generations, process exits, power/session events, and explicit administrative cancellation; heartbeat intervals report liveness only.
- `REQ-DISP-028`: GPU-driver reset, hybrid-GPU adapter migration, dock or external-monitor hotplug, and display-identity renumbering force a fresh all-path inventory and name mapping before Beacon reapplies an owned topology. Cached `DISPLAYx` names are never treated as durable identity.
- `REQ-DISP-029`: Topology or driver repair preserves the owned application and session whenever the inactive-client **AND** no-owned-work cleanup gate is false. Repair cannot silently convert a retained session into teardown.
- `REQ-DISP-030`: Unexpected Beacon Service, StreamWorker, HostAgent, or recovery-supervisor termination leaves enough durable evidence for the surviving or restarted owner to restore or resume deterministically without deleting unrelated displays. If client or owned-work state cannot be proven after a crash, recovery restores physical control but preserves the journaled lease and application until the inactive-client **AND** no-owned-work condition can be recomputed.
- `REQ-DISP-031`: Recovery diagnostics expose the triggering Windows or driver event, journal revision, exact owned identities, baseline source, selected compensation order, every observed topology generation, and final verification result.
- `REQ-DISP-032`: Beacon cannot claim its virtual-display pipeline more robust than an upstream implementation until the production recovery matrix passes on the target laptop and at least one clean Windows installation.
- `REQ-DISP-033`: Every implementation stage that can mutate Windows display or driver state captures a verified physical baseline and arms an independent recovery owner before its first mutation.
- `REQ-DISP-034`: Every such stage ends by proving the internal physical panel is active and primary at its captured baseline resolution, refresh, orientation, and aspect ratio; mirror mode is disabled; no inactive-session Beacon lease or virtual output remains; the driver control session is closed; and the input desktop is locally usable.
- `REQ-DISP-035`: A failed physical-display integrity closeout fails the entire stage, blocks all later work, retains diagnostics, and invokes compensating recovery. A successful feature assertion cannot override a failed laptop-integrity assertion.
- `REQ-DISP-036`: Driver selection is evidence-driven. Beacon first probes the current SudoVDA path, then evaluates an adapted Nonary `libvirtualdisplay`/Vibeshine driver primitive when SudoVDA cannot prove the required HDR and lifecycle contracts. Beacon does not silently replace its product architecture with Vibeshine.

### Session Ownership And Cleanup

- `REQ-SESS-001`: A session owns the process launched by Beacon.
- `REQ-SESS-002`: A session owns traceable child processes.
- `REQ-SESS-003`: A session may own new top-level windows created after launch and remaining on its leased display.
- `REQ-SESS-004`: Unrelated processes and unrelated update windows cannot block cleanup.
- `REQ-SESS-005`: Quit closes or terminates only owned session work unless the user explicitly selects a broader administrative recovery action.
- `REQ-SESS-006`: Disconnect and reconnect without a new app launch preserve coherent ownership and avoid display churn.
- `REQ-SESS-007`: Once the client is inactive and the owned set is empty, Beacon stops StreamWorker, restores physical primary, and removes the eligible lease.
- `REQ-SESS-008`: Launch or streaming failure executes compensating actions in reverse order and verifies the final physical state.
- `REQ-SESS-009`: Default disconnect retains active-client state; explicit inactive disconnect evaluates the same cleanup gate as quit.
- `REQ-SESS-010`: StreamWorker exit is an observed session failure, not proof that display or application cleanup succeeded.

### Game And Application Collection

- `REQ-GAME-001`: The APK selects applications from one server-owned normalized catalog.
- `REQ-GAME-002`: Providers include Steam official games, Steam non-Steam shortcuts, Heroic, Hydra, and manual Windows entries.
- `REQ-GAME-003`: Steam non-Steam shortcuts use the correct 64-bit `steam://rungameid/...` identity.
- `REQ-GAME-004`: External games injected into Steam launch without duplicate manual streaming-server mappings.
- `REQ-GAME-005`: Catalog entries contain launch identity, source, installed state, artwork, and process-tracking hints.
- `REQ-GAME-006`: Catalog entries do not own display or streaming policy in version one.
- `REQ-GAME-007`: SteamGridDB is used when available and configured.
- `REQ-GAME-008`: Multiple exact-name artwork candidates are evaluated until usable artwork is found.
- `REQ-GAME-009`: Missing external artwork produces a readable generated title cover.
- `REQ-GAME-010`: Automatic discovery deduplicates obsolete manual mappings.

### Streaming Data Plane

- `REQ-STREAM-001`: The product exposes one versioned Beacon streaming protocol between StreamWorker and StreamCore.
- `REQ-STREAM-002`: The protocol is private to Beacon version one and carries no upstream compatibility guarantee.
- `REQ-STREAM-003`: The session handshake authenticates a single-use Beacon ticket before media or input is accepted.
- `REQ-STREAM-004`: Video, audio, control, input, metrics, and shutdown belong to one coherent session lifecycle.
- `REQ-STREAM-005`: Media transport supports ordered frame reconstruction, bounded reordering, explicit loss evidence, and recovery suitable for low-latency gaming.
- `REQ-STREAM-006`: Reliable control must not cause video head-of-line blocking.
- `REQ-STREAM-007`: The version-one protocol and production Worker support H.264, HEVC, and AV1 through the same contract rather than alternate routes. H.264 is the bootstrap and prerelease path, not the final codec ceiling.
- `REQ-STREAM-008`: The protocol supports SDR first and extends the same path to 10-bit HDR.
- `REQ-STREAM-009`: Audio and input use the same authenticated session identity as video.
- `REQ-STREAM-010`: Stream state changes are event-driven; no descriptor polling, startup sleep, cancellation timeout, or watchdog owns lifecycle.
- `REQ-STREAM-011`: StreamWorker readiness is acknowledged over private IPC before Beacon publishes a connectable session.
- `REQ-STREAM-012`: StreamCore stop releases transport, decoder, audio, input, Surface, and native resources exactly once.
- `REQ-STREAM-013`: No launch URI, Android intent, endpoint-role map, RTSP session URL, wrapper manifest, or runtime descriptor file appears in the production client contract.
- `REQ-STREAM-014`: Low-level transport libraries may be reused internally, but users and higher-level Beacon modules see only the Beacon protocol.

### Input Capabilities

- `REQ-INPUT-001`: Each client reports touch, keyboard, mouse, controller, and Android-device motion capabilities independently; Beacon has no global or mutually exclusive input mode.
- `REQ-INPUT-002`: A session may carry every input category supported by that client without one category disabling another.
- `REQ-INPUT-003`: Version-one motion input originates from the Android device IMU, independently of any attached controller adapter; controller-provided motion is not required.
- `REQ-INPUT-004`: Motion samples carry a monotonic source timestamp, three-axis angular velocity, three-axis acceleration, and an explicit normalized coordinate system. Beacon never fabricates motion capability when either required sensor is absent.
- `REQ-INPUT-005`: Motion enablement, calibration, sensitivity, and axis inversion are client-local interaction settings and do not alter server stream policy.
- `REQ-INPUT-006`: The Windows host publishes the active client's motion as a DSU/Cemuhook-compatible UDP server on loopback, using `127.0.0.1:26760` by default, so Cemu-class emulators can consume it independently from the normal virtual-controller route.
- `REQ-INPUT-007`: DSU is an application-level interoperability adapter, not a Windows kernel driver or a replacement gamepad backend. Beacon never silently converts device motion to mouse, right-stick, or virtual-controller input.
- `REQ-INPUT-008`: The DSU adapter is owned by the authenticated Beacon session. Sensor capture and publication start only when that client advertises motion, survive a transient media reconnect with the owning session, and stop deterministically on session quit or administrative termination.
- `REQ-INPUT-009`: Failure to bind the configured DSU endpoint or loss of device-motion input reports motion as unavailable with a concrete diagnostic; it does not fail controller input, streaming, or display restoration.
- `REQ-INPUT-010`: Static tests verify sensor normalization, timestamps, DSU framing, protocol version, CRC, and axis conversion. Dynamic tests use a loopback DSU client that performs the same version, port-list, subscription, and motion-data exchange expected by Cemu; physical-device acceptance verifies orientation, latency, and drift.

The interoperability boundary follows the documented
[Cemuhook Android motion-source model](https://cemuhook.sshnuke.net/padudpserver.html). Current Cemu is
a DSU client whose default provider is `127.0.0.1:26760` and whose motion integration consumes both
acceleration and gyroscope fields from DSU data responses. Beacon therefore transports Android sensor
samples inside its authenticated session and publishes DSU only on the Windows loopback boundary.

### HDR

- `REQ-HDR-001`: HDR is best-effort capability work and cannot destabilize SDR sessions.
- `REQ-HDR-002`: Server policy supports `off`, `prefer`, and `require`.
- `REQ-HDR-003`: `off` selects SDR even if the chain supports HDR.
- `REQ-HDR-004`: `prefer` selects HDR only when every required boundary is proven; otherwise it selects SDR and records the missing boundary.
- `REQ-HDR-005`: `require` fails before launch when the complete chain is unavailable.
- `REQ-HDR-006`: The complete chain is driver/virtual display, Windows Advanced Color, capture, 10-bit conversion, encoder, Beacon protocol metadata, decoder, and client display presentation.
- `REQ-HDR-007`: Beacon never fabricates HDR capability when Windows or Android reports SDR.
- `REQ-HDR-008`: HDR activation and fallback reasons appear in the plan and operational journal.
- `REQ-HDR-009`: Production capability is reported only from production-path evidence. Mock endpoint capabilities, planner tests, API success, or an accepted Advanced Color request cannot establish HDR support.
- `REQ-HDR-010`: HDR acceptance runs with server policy `require` and proves all boundaries in `REQ-HDR-006`, including Windows reporting HDR supported and active with at least 10 bits per channel, a 10-bit encoded bitstream with correct color metadata, and Android reporting HDR presentation on the physical display.
- `REQ-HDR-011`: The production Worker must advertise `hdr10=true` only when its selected capture, conversion, encoder, and protocol path can execute that contract. The Android client must advertise HDR10 only when decoder, 10-bit vector, display, and presentation evidence all pass.
- `REQ-HDR-012`: HDR requires at least one production 10-bit codec path. HEVC Main10 is the default implementation candidate; AV1 10-bit may be selected first when measured server and client evidence makes it the viable path. H.264 SDR cannot satisfy HDR acceptance.
- `REQ-HDR-013`: If the driver, Windows, GPU encoder, protocol, decoder, or client display blocks HDR after candidate evaluation, Beacon records the exact failing boundary, preserves working SDR behavior, and stops for a product decision. It does not declare HDR complete through fallback.

### Recovery And Diagnostics

- `REQ-REC-001`: Recovery remains a first-class product capability, not a substitute for correct lifecycle behavior.
- `REQ-REC-002`: Cockpit actions include stop stream, restore physical primary, move and minimize windows, close windows, terminate owned processes, remove a lease, and reset topology.
- `REQ-REC-003`: Owning APK emergency actions include stop and restore for its active session.
- `REQ-REC-004`: Cockpit recovery supports UAC elevation when the target process or display action requires it.
- `REQ-REC-005`: Diagnostics expose benchmark evidence, plan decisions, display readiness, topology transitions, StreamWorker readiness, capture/encoder selection, media health, input health, and cleanup results.
- `REQ-REC-006`: Errors identify the failing ownership boundary and the attempted compensating action.
- `REQ-REC-007`: Root repair and safe automatic recovery take priority over merely improving error text.
- `REQ-REC-008`: Operational snapshots contain generic Beacon state and no wrapper-specific or upstream-protocol-specific fields.

### Testing Without A Phone

- `REQ-TEST-001`: Most development and validation uses fakes, Client Lab, CLI simulation, Windows probes, and Android emulator.
- `REQ-TEST-002`: Client Lab simulates registration, benchmark inventory/results, catalog selection, launch, input, disconnect, reconnect, stop, and emergency recovery.
- `REQ-TEST-003`: A fake Z Fold 7 profile preserves `2560x1600` and `120 Hz` intent.
- `REQ-TEST-004`: Fast tests cover planning, benchmark interpretation, display lifecycle, ownership cleanup, recovery sequencing, and catalog normalization.
- `REQ-TEST-005`: StreamWorker and StreamCore have deterministic in-memory transport boundaries for packet loss, reordering, cancellation, and lifecycle tests.
- `REQ-TEST-006`: Android emulator runs the real APK, StreamCore, Surface decoder, benchmark workflow, catalog selection, launch, stop, and reconnect.
- `REQ-TEST-007`: Real Windows display integration tests remain explicit and manually runnable because they change topology.
- `REQ-TEST-008`: A production vertical-slice test runs in an isolated environment containing only Beacon and its declared platform prerequisites, and proves Beacon-owned capture to emulator presentation. Workstation validation is confined to Beacon's dependency graph, packaged artifacts, process tree, and owned endpoints; it must not enumerate, query, trace, or control another installed streaming product.
- `REQ-TEST-009`: Physical phone testing is reserved for final decoder quality, 120 Hz, HDR, thermals, Wi-Fi behavior, touch, controllers, gyroscope, audio, and human experience.
- `REQ-TEST-010`: Static checks prevent upstream compatibility types, wrapper configuration, and duplicate Android routes from re-entering protected boundaries.
- `REQ-TEST-011`: Deterministic recovery tests inject service, Worker, HostAgent, and recovery-supervisor termination at every display lifecycle phase and verify journal-driven compensation without deleting an unowned display.
- `REQ-TEST-012`: Deterministic Windows-boundary tests inject power/session events, PnP disable/re-enable, driver reconnect, GPU reset, hotplug, topology renumbering, and missing-current-snapshot conditions through production interfaces.
- `REQ-TEST-013`: Guarded real-topology tests prove normal quit, rapid reconnect, lock/unlock, sleep/resume, hibernate/resume, sign-out/sign-in, service restart, HostAgent restart, GPU-driver restart, and dock/external-monitor changes from a recorded physical baseline back to independently verified physical control.
- `REQ-TEST-014`: Every destructive real-topology test arms an independent production-equivalent recovery owner before mutation and records restore-before-remove plus remove-before-restore behavior where both orders are applicable.
- `REQ-TEST-015`: Recovery acceptance verifies that owned applications survive topology repair while active and that inactive **AND** no-owned-work remains the only automatic lease-removal condition.
- `REQ-TEST-016`: Clean-machine validation installs the driver and HostAgent, creates and recovers a lease, exercises a verified driver update and rollback, uninstalls Beacon, and leaves the pre-install physical topology intact.
- `REQ-TEST-017`: Capability status uses the ordered states `absent`, `modeled`, `static-tested`, `emulator-validated`, `target-laptop-validated`, and `physical-client-validated`. Reporting cannot collapse these states into a single `implemented` or `validated` label.
- `REQ-TEST-018`: Every core-stage artifact set contains the pre-mutation physical baseline, driver and binary identities, production-path capability evidence, failure injections, final topology snapshot, and independent laptop-integrity closeout.

## Beacon Session Contract

Beacon Service creates two representations from one immutable plan:

1. A private StreamWorker command containing capture target, encoder/audio configuration, transport policy, input permissions, benchmark mode, and session identity.
2. A public APK session envelope containing Beacon protocol version, worker address, single-use ticket, selected media facts, and plan explanation.

Neither representation contains user-editable policy. The public envelope contains no executable path, upstream protocol field, launch URI, app-list id, wrapper state, or long-lived secret.

The startup transaction is:

```text
validate benchmark evidence
-> compute immutable plan
-> prepare/verify display
-> start StreamWorker
-> wait for IPC readiness event
-> activate planned display
-> launch selected application
-> issue single-use APK ticket
-> accept authenticated StreamCore session
```

Failure runs compensating actions for only the steps that succeeded, in reverse order. Cleanup verification is part of the operation result.

## Benchmark Lifecycle

### Full Calibration

Full calibration is automatically triggered by a material network/hardware/schema change and manually available from the APK. It contains:

1. Passive device and display capability inventory.
2. Reliable transfer of versioned decode vectors.
3. Local decode/presentation tests to isolate hardware behavior.
4. Production-transport network rounds.
5. End-to-end streamed decode rounds through StreamWorker and StreamCore.
6. Sustained candidate workloads for thermal stability.
7. Server-side scoring and persistence.

### Session Preflight

Every launch performs a short current-path measurement using the production transport. It verifies that the selected plan still fits current RTT, jitter, loss, throughput, decoder, display, and thermal facts. A failed preflight causes Beacon to recompute or reject the plan before display and application side effects.

### Benchmark Result Ownership

The APK displays progress and factual results. Beacon Service stores raw evidence, evaluates candidate modes, and records the selected plan. A client-side score is never authoritative.

## Source Reuse Rules

Candidate upstream primitives include:

- Windows DXGI/WGC capture.
- GPU texture conversion and scaling.
- NVENC, AMF, Quick Sync, Media Foundation, and software encoding adapters.
- Audio capture and encoding.
- Packetization, FEC, congestion, jitter, and frame-recovery algorithms.
- Android codec and rendering adapters.
- Windows input injection and controller support.
- SudoVDA integration and topology lessons.
- Vibeshine/Vibepollo recovery-registry ownership, driver re-enable/restart, exact-device reclaim,
  display-database restoration, and hybrid-GPU recovery behavior. Candidate references include
  [`virtual_display_recovery_registry.h`](https://github.com/Nonary/vibeshine/blob/vibe/src/platform/windows/virtual_display_recovery_registry.h),
  [`virtual_display_cleanup.cpp`](https://github.com/Nonary/vibeshine/blob/vibe/src/platform/windows/virtual_display_cleanup.cpp),
  and [`virtual_display_sunshine.cpp`](https://github.com/Nonary/vibeshine/blob/vibe/src/platform/windows/virtual_display_sunshine.cpp).

Each candidate is classified before retention:

- `keep`: already generic and aligned.
- `adapt`: valuable primitive with upstream policy or protocol removed.
- `replace`: required capability with an unsuitable implementation.
- `delete`: compatibility, duplicate, dead, or transitional infrastructure.

No source is retained merely because tests already exist or implementation effort was previously spent.

## Architecture Recovery Program

### Recovery Gate 0: Freeze And Inventory

- Freeze new product features.
- Remove the unsynced Apollo host-provisioning work.
- Produce a source and dependency inventory for Core, Server, Platform.Windows, Cockpit, Android, probes, tests, and documentation.
- Classify each streaming-related component as keep, adapt, replace, or delete.
- Add protected-boundary checks before destructive refactoring.

### Recovery Gate 1: Restore Core Boundaries

- Remove `MoonlightNativeSessionDescriptor` and every upstream-protocol type from Beacon Core.
- Replace wrapper-shaped health and session contracts with small Beacon-owned records.
- Remove launch URIs, endpoint-role maps, manifests, descriptor files, wrapper child configuration, and external-process production modes.
- Keep fake implementations behind the same target contracts.

### Recovery Gate 2: Collapse Android To One Path

- Remove Java GameStream RTSP/RTP/UDP/depacketization routes.
- Remove the Moonlight compatibility module and native session mapping.
- Preserve generic MediaCodec, Surface, audio, input, lifecycle, benchmark, catalog, and control-plane primitives when they remain aligned.
- Introduce one empty/fake StreamCore boundary before real transport implementation.

### Recovery Gate 3: Define Worker And Protocol

- Complete the upstream primitive audit.
- Define versioned Service-to-Worker IPC and Worker-to-StreamCore contracts.
- Build StreamWorker and StreamCore as Beacon-owned modules.
- Prove startup, readiness, stop, crash isolation, and secret redaction using deterministic fakes.

### Recovery Gate 4: Benchmark Vertical Slice

- Implement automatic network/hardware change detection.
- Implement manual benchmark.
- Run benchmark traffic through the production Beacon transport boundary.
- Store and interpret raw results in Beacon Service.
- Verify the workflow in Client Lab and Android emulator.

### Recovery Gate 5: Minimal Real Stream

- Capture one planned display.
- Encode H.264 SDR.
- Stream through the single Beacon protocol.
- Decode and present through StreamCore on `emulator-5554`.
- Select and launch one server-catalog application.
- Stop, reconnect, and restore through Beacon-owned components with no Apollo or Sunshine dependency.

Gate 5 proved the first H.264 SDR integration transaction. It does not authorize secondary feature
expansion. Execution next follows Core Stage 1 driver hardening and Core Stage 2 HDR closure in the
authoritative core-hardening plan. Audio, controller, and benchmark work already present remains
preserved, but new work on richer input, remaining codecs, packaging, multi-client behavior, or UI
refinement stays frozen until both core stages close.

## Deletion Targets

The following are explicit removal targets unless the recovery inventory proves a small generic primitive can be extracted first:

- `ExternalProcessStreamingBackend` and external wrapper configuration.
- `Beacon.StreamingProbe` as a wrapper/child-process harness.
- Wrapper manifest and runtime descriptor contracts.
- `MoonlightNativeSessionDescriptor` and public `nativeSession` responses.
- GameStream/Moonlight endpoint maps, RTSP URLs, launch URIs, and Android intent fallback.
- Java GameStream RTSP, RTP, UDP, depacketization, and replacement route.
- Vendored Moonlight compatibility modules and their pairing/session assumptions.
- Apollo host identity, pairing, app-list, launch, and cancel work.
- Backend selection UI/configuration whose only purpose is compatibility or wrappers.
- Tests and documentation that assert removed compatibility behavior rather than target Beacon behavior.

## Non-Goals For Version One

- Apollo, Sunshine, Moonlight, GameStream, Artemis, Vibepollo, or Vibeshine compatibility.
- User-selectable streaming backends or transports.
- A generic plugin ecosystem.
- Multiple simultaneous production transport implementations.
- APK editing of stream, display, or server policy.
- Per-game display-topology overrides.
- Internet relay, public matchmaking, or arbitrary untrusted users.
- Browser streaming.
- Mirror mode.
- Full live adaptation before the initial benchmark-driven planner is stable.
- Forcing HDR on a client or server boundary that reports no HDR support. Working SDR remains the
  fallback, but proving or decisively rejecting the product's HDR path is a core-hardening gate.

## Acceptance Gates

### Architecture Recovery

- Beacon Core contains no Moonlight, GameStream, Apollo, Sunshine, RTSP, wrapper, manifest, launch-URI, or external-process streaming contract.
- Production server registration exposes one Beacon StreamWorker path.
- Generic health and session records contain no wrapper-specific fields.
- The APK has one StreamCore route and no compatibility fallback.
- Compatibility configuration is absent from checked settings, environment variables, WPF, and diagnostics.
- All retained upstream code has provenance and a narrow Beacon-owned boundary.
- Existing control-plane, display, game-library, ownership, recovery, and local-settings behavior remains covered while obsolete compatibility tests are removed.

### Benchmark

- Emulator and fake clients can complete full and preflight benchmark workflows.
- Network-change events automatically invalidate the appropriate benchmark fingerprint.
- Manual benchmark always starts a new run.
- Hardware and network raw evidence reaches Beacon Service without client-side policy selection.
- Planner decisions cite the benchmark evidence revision.
- The APK exposes no stream-setting controls.

### Minimal Beacon-Owned Stream

- Beacon-local dependency inspection and runtime evidence prove no external streaming control plane or runtime is integrated. Evidence is limited to Beacon's source dependencies, packaged artifacts, process tree, and owned endpoints; installed third-party streaming products are not probed.
- APK selects an application from the Beacon catalog.
- Beacon computes a complete plan before side effects.
- Beacon prepares the correct per-client display without mirror or physical fallback.
- StreamWorker reports ready through private IPC.
- Emulator authenticates with one Beacon ticket and shows moving H.264 video from the planned display.
- Stop and reconnect use the same path without parallel fallback.
- Session cleanup restores verified physical-primary state under the server-owned **inactive AND no-owned-work** rule.
- Logs and snapshots expose Beacon state without secrets or compatibility artifacts.

### Core Stage 1: Driver Capability And Recovery

- The selected driver proves HDR-capable IddCx/Advanced Color behavior before Beacon invests in its
  long-term lifecycle integration.
- Driver install, update, rollback, lease creation, heartbeat, exact-identity reconciliation, and
  removal are serialized through the HostAgent boundary.
- A durable HostAgent-owned journal and production recovery supervisor compensate component crashes,
  power/session transitions, PnP faults, driver restart, GPU reset, and topology identity changes.
- The target-laptop recovery matrix and one clean-machine driver lifecycle complete without deleting
  an unowned display or terminating owned work.
- The stage ends with the mandatory physical-display integrity closeout in `REQ-DISP-034`.

### Core Stage 2: End-To-End HDR

- A virtual display is created for the physical client at its planned aspect-correct mode and Windows
  reports HDR supported and active with at least 10 bits per channel.
- Production capture, conversion, one 10-bit codec, Beacon transport metadata, Android decoding, and
  physical-display presentation pass under `HdrPreference.Require`.
- The retained evidence distinguishes every HDR boundary and proves that no SDR fallback satisfied
  the HDR assertion.
- `off` and `prefer` still prove stable SDR behavior, and an unsupported client receives a recorded
  fallback reason without topology churn.
- The stage ends with the mandatory physical-display integrity closeout in `REQ-DISP-034`.
- If any boundary is not technically viable after the planned candidate evaluation, the stage exits
  as `decision-required`, not passed, and further feature work remains frozen.

### Physical Device Final Confirmation

- Z Fold 7 benchmark selects a sustainable network/hardware profile.
- `2560x1600` intent is preserved.
- 120 FPS is selected only when the complete measured path sustains it.
- Touch, controller, keyboard, mouse, gyroscope, audio, thermals, Wi-Fi behavior, HDR, and human experience are validated on hardware.

## Definition Of Completion

Beacon version one is complete when it can be installed without Apollo or Sunshine, register the APK, automatically benchmark the current network and hardware, show the normalized Windows app/game catalog, compute a server-owned plan, create and activate the correct per-client virtual display, launch the selected application, stream H.264, HEVC, or AV1 through one Beacon-owned data plane according to per-client evidence, present HDR when the complete measured chain supports it, accept client input, stop or recover safely, and restore the laptop to a verified physical-primary state after normal termination, component failure, power/session transitions, driver restart, and material display-topology change.

No compatibility layer, wrapper mode, alternate Android route, or client-side streaming policy is required to achieve that result.
