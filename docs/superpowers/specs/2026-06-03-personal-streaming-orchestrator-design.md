# Personal Streaming Orchestrator Design

## Purpose

Build a new personal game-streaming system from scratch, using proven pieces from Sunshine, Apollo, Vibeshine, Vibepollo, and the Apollo rescue work as source material. The goal is not to make a heavier Apollo fork. The goal is a simpler, server-authoritative architecture where the Windows server knows each client in advance, prepares the right virtual desktop before a session starts, selects the best streaming plan from capabilities and live telemetry, and exposes a clean WPF control surface.

The Android APK must be thin for execution but useful for preflight control. It identifies the device, edits only that device's server-side client profile, reports live capabilities and telemetry, receives a server-computed session plan, and then streams/decodes/input-forwards according to that plan.

## Core Thesis

The current Apollo/Artemis model pushes too much important information into connection-time negotiation. The client reports settings while Apollo is already preparing display topology, encoder state, virtual display state, app launch, and stream transport. This creates race windows and duplicate configuration.

The new system must invert that model:

```text
Client identifies itself
-> server loads client profile
-> server observes live endpoint/network facts
-> server computes a session plan
-> server prepares virtual desktop and launch environment
-> client starts stream using the plan
```

The server owns desired state. The client reports facts and preferences. The server computes executable state.

## Project Identity And Repository

The project name is **Beacon Stream**. The product can be shortened to **Beacon** where that reads better in UI, service names, and logs.

The name is intentional: the client continuously announces presence and intent to the server before a session starts. This is related to the "beaconing client" idea from the Apollo/Artemis debugging work, not a new dependency on Apollo, Artemis, Sunshine, or Moonlight infrastructure.

Repository and namespace decisions:

- Create a new public GitHub repository: `Darkaxt/beacon-stream`.
- Do not make this project an Apollo fork. Keep the Apollo fork only for Apollo-specific fixes.
- Use a public GPL-3.0 license from day one if any Sunshine, Apollo, Vibeshine, or Vibepollo code is copied or adapted.
- Keep copied source, wrapped source, and rewritten source clearly documented in the extraction map.
- Use project names such as `Beacon.Server`, `Beacon.Core`, `Beacon.Desktop`, `Beacon.ClientLab`, and `Beacon.Android`.

The project is allowed to reuse proven implementation pieces, but it is not constrained by the current Sunshine, Apollo, Artemis, Vibeshine, or Vibepollo architecture. Those projects are inputs, not the architecture.

## Non-Goals For Version 1

- No global server settings edited from the APK.
- No per-game virtual desktop overrides.
- No fully bespoke streaming stack at the start.
- No settings explosion copied from Vibepollo/Vibeshine.
- No requirement to support arbitrary public users or unknown device classes.
- No requirement to preserve Apollo's current web UI model.
- No requirement to keep Artemis as the primary configuration source.
- No requirement to make HDR a hard blocker. HDR is a capability to detect, report truthfully, and use when available.
- No requirement for the virtual desktop resolution to match a game's internal render resolution.
- No requirement for a real Android phone during the first implementation milestones.

## Requirement Register

This section is the implementation contract. If a later plan contradicts this register, the register wins unless it is explicitly revised.

### Project Scope

- `REQ-PROJ-001`: Beacon Stream is personal-use first. It must optimize for one trusted Windows server and known personal clients before supporting arbitrary users.
- `REQ-PROJ-002`: The first known endpoint is the Z Fold 7 profile, but the design must support more registered clients later.
- `REQ-PROJ-003`: The project must be built as a new public repository, not as a long-lived Apollo fork.
- `REQ-PROJ-004`: The project must stay source-license honest. Any copied or adapted GPL source keeps the project GPL-3.0 compatible.
- `REQ-PROJ-005`: The first implementation target is the control plane and fake pipeline. Real streaming backend and real Android APK are later milestones.
- `REQ-PROJ-006`: Existing projects are lego pieces. Do not inherit settings-heavy UX or architecture constraints just because they exist upstream.
- `REQ-PROJ-007`: Milestone 0 must begin by creating the public `Darkaxt/beacon-stream` remote repository with `gh`.
- `REQ-PROJ-008`: The working tree must be initialized from or immediately connected to that remote before substantial implementation starts.

### Implementation And Sync Workflow

- `REQ-SYNC-001`: Ongoing implementation must follow this loop: implement, validate static and dynamic checks, sync, refactor, validate static and dynamic checks again, sync.
- `REQ-SYNC-002`: Sync means commit a coherent checkpoint locally and push it to the GitHub remote.
- `REQ-SYNC-003`: The first sync must happen immediately after the remote repository and initial scaffold are created.
- `REQ-SYNC-004`: No substantial objective work may remain local-only across context compactions.
- `REQ-SYNC-005`: `gh` is the required tool for creating the remote GitHub repository and the preferred tool for later GitHub operations.
- `REQ-SYNC-006`: Each synced checkpoint must keep docs, implementation, and validation status aligned with what actually landed.

### Control Plane Ownership

- `REQ-CTRL-001`: The server is the source of truth for desired state.
- `REQ-CTRL-002`: The APK reports identity, facts, capabilities, preferences, telemetry, and user intent.
- `REQ-CTRL-003`: The APK must be able to update its own basic server-side client profile before starting a connection or launch.
- `REQ-CTRL-004`: The APK must not edit global server settings in version 1.
- `REQ-CTRL-005`: The APK must not edit per-game virtual desktop behavior in version 1.
- `REQ-CTRL-006`: Client-local input and UI settings stay on the client.
- `REQ-CTRL-007`: Anything that affects virtual desktop behavior is server-side state.
- `REQ-CTRL-008`: The server must compute a complete effective session plan before display topology, app launch, or streaming starts.
- `REQ-CTRL-009`: The client consumes the session plan. It must not reinterpret display topology, codec, FPS, or recovery policy locally.
- `REQ-CTRL-010`: The WPF cockpit may edit server-global settings, client profiles, recovery actions, and diagnostics because it is a local server-admin surface.
- `REQ-CTRL-011`: APK profile patches must be server-validated against an explicit allowlist.
- `REQ-CTRL-012`: Display mode, blackout, mirror prohibition, persistence, destruction, restore, and recovery safety policies are WPF/server-admin controlled in version 1.

### Client Profile

- `REQ-PROFILE-001`: Each registered client has a server-side client profile.
- `REQ-PROFILE-002`: The profile stores preferred virtual desktop resolution, refresh rate, HDR preference, codec preference, quality/latency preference, optional bitrate cap, audio mode, display behavior policy, and disconnect/end-session preference.
- `REQ-PROFILE-003`: The Z Fold 7 default profile must prefer `2560x1600` and `120 Hz` where the backend can support it.
- `REQ-PROFILE-004`: The profile must preserve 16:10 intent. It must not silently collapse `2560x1600` into `2560x1440`.
- `REQ-PROFILE-005`: The profile must support a policy for using a virtual-primary display while keeping the physical display extended or restored.
- `REQ-PROFILE-006`: The profile must support physical-display-blackout as an optional policy, and blackout must have a recovery path.
- `REQ-PROFILE-007`: The APK stores touch layout, multitouch gestures, controller overlay, haptics, local UI density, wake lock, local theme, and local decoder UI preferences locally.

### Virtual Display Lifecycle

- `REQ-DISP-001`: Virtual desktop identity is per client. Do not reuse one virtual display across multiple clients.
- `REQ-DISP-002`: A client must be able to have one leased virtual display prepared for that client's session lifecycle.
- `REQ-DISP-003`: The leased display must be created or verified during preflight before app launch, not after the stream has already started.
- `REQ-DISP-004`: Launches for a client must bind to that client's leased virtual display.
- `REQ-DISP-005`: Disconnect alone must not imply virtual display teardown.
- `REQ-DISP-006`: Stream stop and display cleanup are separate operations.
- `REQ-DISP-007`: Remove a client virtual display only when the client is no longer active **AND** no owned app, child process, or tracked window remains for that client.
- `REQ-DISP-008`: No timeout may replace the `client inactive AND nothing owned remains` rule.
- `REQ-DISP-009`: Manual recovery can override lifecycle rules when explicitly requested by the user from WPF or by the owning APK emergency action.
- `REQ-DISP-010`: Mirror mode is prohibited by default.
- `REQ-DISP-011`: The server must never silently fall back to streaming or launching on the physical display when the requested virtual display is unavailable.
- `REQ-DISP-012`: If the virtual display is unavailable, the server must attempt safe preflight repair before failing.
- `REQ-DISP-013`: If repair fails, the failure must be explicit and diagnostic, not hidden behind a generic launch error.
- `REQ-DISP-014`: The server must verify that the physical display is primary again after session end or recovery.
- `REQ-DISP-015`: Physical-primary restore must be reconciled and retried when Windows reports a stale or wrong topology.
- `REQ-DISP-016`: The server must prevent the laptop panel from being stranded as inactive when no session owns that state.
- `REQ-DISP-017`: The server must support keeping the virtual display present while an owned app still runs there, without stealing the laptop's main display.
- `REQ-DISP-018`: Display topology decisions must be logged with before/after state, selected display id, resolution, refresh, primary flag, HDR flag, and reason.

### Session Ownership And Cleanup

- `REQ-SESS-001`: A session owns the process launched by the orchestrator.
- `REQ-SESS-002`: A session owns child processes of the launched process when they can be traced.
- `REQ-SESS-003`: A session must classify new top-level windows that appeared after session start and are still on that client's virtual display as session-owned unless an exclusion rule says otherwise.
- `REQ-SESS-004`: Unrelated processes or unrelated update windows must not block cleanup.
- `REQ-SESS-005`: Quit-session behavior must close or terminate only owned session work unless the user explicitly requests a broader recovery action.
- `REQ-SESS-006`: Disconnect/reconnect without launching a new app must keep session state coherent and must not churn displays.
- `REQ-SESS-007`: Launch-app, close-app, disconnect must restore the physical desktop once the owned app/window/process set is empty.
- `REQ-SESS-008`: App launch and stream startup failures must not leave the physical display inactive or the system in mirror mode.

### Display Modes

- `REQ-MODE-001`: The standard remote-gaming mode is virtual display primary for the game/session.
- `REQ-MODE-002`: The physical display must become secondary/extended or restored according to server policy, not disappear accidentally.
- `REQ-MODE-003`: Physical-display blackout may exist as a mode, but it must be optional, visible in the plan, and recoverable.
- `REQ-MODE-004`: If blackout is enabled, the owning APK and WPF cockpit must both be able to request emergency restore.
- `REQ-MODE-005`: The server must explain whether it selected virtual-primary, extended, blackout, or SDR fallback and why.

### HDR

- `REQ-HDR-001`: HDR is best-effort capability work, not a version 1 stability blocker.
- `REQ-HDR-002`: HDR must be represented as an explicit client preference with at least `off`, `prefer`, and `require` modes.
- `REQ-HDR-003`: In `off`, the server must select SDR even if HDR is possible.
- `REQ-HDR-004`: In `prefer`, the server must select HDR only if the full chain reports support; otherwise it must select SDR and explain why.
- `REQ-HDR-005`: In `require`, the server must fail before launch if HDR cannot be provided.
- `REQ-HDR-006`: The full HDR chain is: virtual display driver capability, Windows Advanced Color exposure, capture path, encoder 10-bit support, protocol metadata, client decoder support, and client display support.
- `REQ-HDR-007`: The virtual display driver must truthfully report HDR/WCG capability. Do not fake success in Apollo/Beacon if Windows still reports SDR only.
- `REQ-HDR-008`: If Windows or the driver reports no HDR capability, log the missing boundary and continue SDR unless the profile requires HDR.
- `REQ-HDR-009`: HDR negotiation must not destabilize display creation, topology restore, or stream startup.
- `REQ-HDR-010`: The planner must include HDR status and reason in the effective session plan.

### Network And Stream Planning

- `REQ-NET-001`: The server must estimate endpoint capabilities, endpoint load, and network quality before launch when the required facts are available.
- `REQ-NET-002`: The client must report live telemetry such as RTT, packet loss estimate, Wi-Fi band if available, decode capability, current screen mode, battery, thermal hints, and local decoder load if known.
- `REQ-NET-003`: The server must choose codec, FPS, bitrate, transport, and congestion policy from client profile plus live telemetry.
- `REQ-NET-004`: Version 1 does not need full live network adaptation to prove the architecture, but the planner must be testable with fake telemetry profiles.
- `REQ-NET-005`: The plan must make 120 FPS explicit when requested and supported.

### Game Library And Launch

- `REQ-GAME-001`: Game collection integration is first-class from the beginning.
- `REQ-GAME-002`: Providers include Steam official apps, Steam non-Steam shortcuts, Heroic, Hydra, and manual entries.
- `REQ-GAME-003`: Steam non-Steam shortcuts must use the correct 64-bit `steam://rungameid/...` value.
- `REQ-GAME-004`: External games injected into Steam must launch from the normalized collection without manual Apollo/Sunshine mapping.
- `REQ-GAME-005`: Game profiles in version 1 may include launch identity, source, cover, installed state, and process tracking hints.
- `REQ-GAME-006`: Game profiles in version 1 must not include display topology policy.
- `REQ-GAME-007`: Artwork must use SteamGridDB when available.
- `REQ-GAME-008`: SteamGridDB search must handle multiple exact-name candidates and choose the first candidate with usable artwork.
- `REQ-GAME-009`: If external artwork is unavailable, generate a readable fallback cover from the title.
- `REQ-GAME-010`: The library must not duplicate old hand-mapped Steam games from Apollo/Sunshine config when automatic discovery already covers them.

### Recovery And Diagnostics

- `REQ-REC-001`: Recovery is a first-class feature.
- `REQ-REC-002`: The WPF cockpit must expose restore physical primary, move windows back, close windows on virtual display, terminate owned processes on virtual display, remove virtual display lease, stop stream, and reset topology actions.
- `REQ-REC-003`: WPF recovery actions must support UAC elevation when needed.
- `REQ-REC-004`: Moving windows back must support minimizing moved windows so recovery does not flood the physical desktop.
- `REQ-REC-005`: The APK may call emergency actions for its own active session.
- `REQ-REC-006`: The WPF cockpit may perform broader local admin recovery than the APK.
- `REQ-REC-007`: Recovery tooling is a user escape hatch. It must not become a substitute for correct orchestrator lifecycle behavior.
- `REQ-REC-008`: Logs must expose display API access failures, driver readiness, virtual display creation result, topology changes, restore attempts, stream backend failures, and selected repair actions.
- `REQ-REC-009`: Error messages must be clear, but root fixes and safe auto-repair are preferred over merely surfacing nicer errors.

### Testing Without Phone

- `REQ-TEST-001`: Most development and validation must not require the real phone.
- `REQ-TEST-002`: Build a Client Lab web app that simulates a remote client control plane.
- `REQ-TEST-003`: Client Lab must simulate hello, profile fetch, allowed profile patch, capability report, telemetry report, plan request, launch request, disconnect, reconnect, quit, and emergency restore.
- `REQ-TEST-004`: Client Lab must include a Z Fold 7 profile with `2560x1600` and `120 Hz`.
- `REQ-TEST-005`: Client Lab must be browser-testable with Playwright.
- `REQ-TEST-006`: A CLI fake endpoint must exist for automated tests and scripted sequences.
- `REQ-TEST-007`: The orchestrator must use interfaces for display, streaming, game library, process/window tracking, and telemetry so fake backends can test policy deterministically.
- `REQ-TEST-008`: Fast tests must validate planner decisions, profile validation, display lifecycle policy, cleanup rules, recovery sequencing, and game library normalization.
- `REQ-TEST-009`: Real Windows/SudoVDA integration tests must be separated from fast tests and manually runnable.
- `REQ-TEST-010`: Real phone testing must be final confirmation for decoder compatibility, input/touch behavior, actual stream quality, real telemetry quality, and human experience.

### First Milestone Limits

- `REQ-M0-001`: Milestone 0 and Milestone 1 must not implement the real streaming backend.
- `REQ-M0-002`: Milestone 0 and Milestone 1 must not implement the real Android APK.
- `REQ-M0-003`: Milestone 0 and Milestone 1 must create the extraction map, architecture docs, core contracts, fake backends, fake endpoint, planner tests, and profile tests.
- `REQ-M0-004`: No source from Sunshine, Apollo, Vibeshine, or Vibepollo may be copied before the license/source boundary is documented.

## Product Shape

The system has four major layers.

### Windows Orchestrator Service

The service is the source of truth. It owns client profiles, game libraries, virtual display lifecycle, session planning, process tracking, telemetry, and recovery actions.

Responsibilities:

- Client registry and pairing.
- Server-side client profiles.
- Capability and telemetry ingestion.
- Effective session plan generation.
- Virtual display creation, activation, persistence, teardown, and restore.
- App/game discovery and launch.
- Session ownership and process/window tracking.
- Streaming backend integration.
- Logs, diagnostics, and recovery endpoints.

### WPF Control App

The WPF app is the local cockpit. It replaces the need to manage a complex web UI and a separate rescue helper.

Responsibilities:

- Show clients and their profiles.
- Show active leases, sessions, displays, and apps.
- Browse normalized game collections.
- Show planner decisions and why they were made.
- Edit server-global settings.
- Edit client profiles locally.
- Run recovery actions: restore physical primary, move windows back, close/terminate stranded apps, remove virtual displays, stop sessions.
- Inspect logs and diagnostics.

The WPF app talks to the orchestrator API. It must not contain core policy that the service also needs.

### Thin Android APK

The APK is not read-only, but it is scoped. In version 1 it may edit only the current device's basic server-side client profile. It may not edit global server settings or per-game display behavior.

Responsibilities:

- Identify the device.
- Authenticate to the server.
- Fetch its server-side client profile.
- Let the user edit basic client preferences before launch.
- Store only local interaction settings.
- Report capabilities and live telemetry.
- Request a session plan.
- Start/stop stream.
- Forward input.
- Trigger emergency restore/terminate commands.

Client-local settings stay local:

- Touch layout.
- Multitouch gestures.
- Controller overlay.
- Haptics.
- Local UI density.
- Wake lock.
- Client-side decoder UI preferences.

The owning APK may edit only these server-side client settings because they affect server planning and are safe to scope to one client:

- Preferred virtual desktop resolution.
- Preferred virtual desktop refresh rate.
- HDR preference.
- Codec preference.
- Quality/latency preference.
- Optional bitrate cap.
- Audio mode preference.
- Disconnect/end-session preference when it does not change display topology policy.

### Native Streaming Core

This is where existing projects provide useful pieces. It must be headless and policy-free.

Candidate source material:

- Sunshine: streaming protocol, capture, encode, audio, input, and stable backend primitives.
- Apollo: virtual display integration, SudoVDA usage, dynamic app discovery, client-aware display lessons.
- Vibeshine/Vibepollo: research material only. Cherry-pick specific, verified improvements. Do not inherit their settings-heavy product model.
- ApolloDisplayRescue: seed concepts for WPF recovery and window/display control.

## Setting Ownership

### Server Global Settings

Editable only from WPF or local server admin tools.

Examples:

- Driver paths and installation state.
- Encoder backend availability.
- Library provider setup.
- SteamGridDB or other API keys.
- Storage paths.
- Default cleanup policy.
- Pairing policy.
- Service startup behavior.
- Logging and diagnostics.
- Recovery safety rules.

### Server-Side Client Profile

Stored on the server. Editable from WPF. Editable from the owning APK only through an explicit version 1 allowlist.

APK-editable in version 1:

- Preferred virtual desktop resolution.
- Preferred virtual desktop refresh rate.
- HDR preference.
- Codec preference.
- Quality/latency preference.
- Optional bitrate cap.
- Audio mode preference.
- Disconnect/end-session preference when it does not change display topology policy.

WPF/server-admin only in version 1:

- Display mode policy.
- Physical display blackout policy.
- Mirror mode prohibition.
- Virtual display persistence policy.
- Virtual display destruction policy.
- Restore physical primary policy.
- Recovery safety rules.

Example with both APK-editable preferences and WPF/admin-only policy fields:

```json
{
  "clientId": "z-fold-7",
  "display": {
    "preferredResolution": "2560x1600",
    "preferredRefreshHz": 120,
    "hdrPreference": "prefer",
    "mode": "virtual-primary",
    "restorePhysicalDisplayOnEnd": true,
    "forbidMirrorMode": true
  },
  "stream": {
    "qualityMode": "auto",
    "codecPreference": "auto",
    "bitrateCapMbps": null
  },
  "audio": {
    "mode": "stereo"
  },
  "session": {
    "keepAppRunningOnDisconnect": false,
    "allowEmergencyRestoreFromClient": true
  }
}
```

### Client-Local Settings

Stored only in the APK.

Examples:

- Touch/multitouch mapping.
- Controller overlay layout.
- Haptic strength.
- Local client theme.
- Local stream UI preferences.
- Local decoder debugging overlay.

## Virtual Desktop Rule

Anything that affects virtual desktop behavior is server-owned state.

This includes:

- Resolution and refresh.
- HDR/SDR preference and fallback.
- Primary vs extended behavior.
- Physical display blackout behavior.
- Mirror mode prohibition.
- Virtual display persistence.
- Creation timing.
- Destruction rules.
- Restore physical primary policy.
- Per-client display identity.
- Window cleanup and migration rules.
- Emergency restore behavior.

The server must preserve the requested aspect-ratio intent. For the Z Fold 7 profile, `2560x1600` is not interchangeable with `2560x1440`. If the requested mode cannot be created, the planner must repair or fail explicitly; it must not silently collapse to a 16:9 mode or fall back to the physical display.

Version 1 must not support per-game virtual desktop overrides. Games can choose internal render resolution in their own settings. The server prepares one stable virtual desktop for the client profile, and games run inside that desktop.

## HDR Capability Model

HDR support is possible only when the whole path supports it:

```text
Virtual display driver advertises HDR/WCG correctly
-> Windows exposes Advanced Color on that display
-> capture path preserves HDR/10-bit data
-> encoder supports HEVC Main10, AV1 10-bit, or another required 10-bit format
-> protocol carries HDR metadata
-> client decoder supports the selected HDR format
-> client panel or TV can render the HDR mode
```

Beacon must model HDR as:

- `off`: force SDR.
- `prefer`: use HDR only if the full chain reports support, otherwise continue SDR with a clear reason.
- `require`: fail before launch if HDR cannot be provided.

HDR must not be treated as a late toggle after display and stream setup. The planner decides HDR before launch, records the decision in the session plan, and logs the first missing boundary when HDR is unavailable.

## Planner Hierarchy

Version 1 planner hierarchy:

```text
Server global constraints
-> client profile
-> live client capabilities
-> live network telemetry
-> effective session plan
```

Game profile chooses what to launch. It does not choose monitor topology.

## Session Plan

The effective session plan is generated by the server before launch.

Example:

```json
{
  "sessionId": "2026-06-03T00:00:00Z-zfold7-dispatch",
  "clientId": "z-fold-7",
  "appId": "steam-shortcut:3767414131",
  "display": {
    "displayId": "client-z-fold-7",
    "resolution": "2560x1600",
    "refreshHz": 120,
    "hdrPreference": "prefer",
    "hdr": false,
    "hdrMode": "sdr",
    "mode": "virtual-primary",
    "reason": "HDR disabled because the virtual display does not report HDR capability"
  },
  "stream": {
    "codec": "av1",
    "fps": 120,
    "initialBitrateMbps": 65,
    "transport": "lan-direct",
    "congestionPolicy": "adaptive"
  },
  "audio": {
    "mode": "stereo"
  },
  "recovery": {
    "restorePhysicalDisplayOnEnd": true,
    "allowClientAbort": true,
    "terminateOwnedAppOnQuit": true
  }
}
```

The APK consumes this plan. It does not reinterpret it.

## Client Lifecycle

### Profile Sync

```text
APK starts
-> POST /clients/hello
-> server identifies client
-> server returns client profile, server capabilities, and editable fields
-> APK may PATCH /clients/{id}/profile
-> server validates and stores allowed fields
```

### Preflight

```text
APK reports live capabilities and telemetry
-> server updates endpoint facts
-> APK asks for a plan for selected app
-> server returns effective plan and explanations
```

Live telemetry examples:

- RTT.
- Packet loss estimate.
- Wi-Fi band if available.
- Decode capability.
- Current screen mode.
- Battery and thermal hints.
- Local decoder load if known.

### Launch

```text
APK accepts plan
-> server ensures virtual display
-> server applies topology
-> server launches app
-> streaming backend starts
-> APK connects to stream
```

### Disconnect

Disconnect does not automatically imply display teardown.

The server evaluates owned state:

- Is the client still active?
- Is a session still active?
- Is the launched app or child process still running?
- Are there new top-level windows on the client's virtual desktop?
- Did the user request quit, keep running, or emergency restore?

Version 1 must keep the lifecycle simple:

```text
Remove virtual desktop only when:
client is no longer active
AND no owned app/process/window remains
```

Manual rescue can override this.

## Game Collection Integration

Game/library integration must be built in from the beginning.

Providers:

- Steam official apps.
- Steam non-Steam shortcuts.
- Heroic.
- Hydra.
- Manual entries.

Normalized game model:

```json
{
  "id": "steam-shortcut:3767414131",
  "title": "Dispatch",
  "source": "steam-shortcut",
  "launch": {
    "type": "steam-rungameid",
    "command": "steam://rungameid/16180920483166814208"
  },
  "artwork": {
    "cover": "C:/ProgramData/Orchestrator/artwork/dispatch.png",
    "source": "steamgriddb"
  },
  "installed": true
}
```

Version 1 game profiles must not include display policy. They may include launch identity, source, cover, installed state, and process tracking hints.

## Recovery Model

Recovery must be first-class, not an afterthought.

Server actions:

- Restore physical desktop primary.
- Move all windows from virtual displays back to physical display.
- Close windows on a virtual display.
- Terminate owned processes on a virtual display.
- Remove virtual display lease.
- Stop active stream.
- Reset topology to a known-good state.

The APK may call emergency actions for its own active session. The WPF app can run broader local admin recovery.

## Source Project Reuse Strategy

### Sunshine

Use as the primary reference for stable streaming primitives:

- GameStream-compatible protocol pieces.
- Capture and encode pipeline.
- Audio and input plumbing.
- RTSP/session mechanics.

### Apollo

Use as the primary reference for:

- SudoVDA integration.
- Virtual display behavior.
- Client-aware display lease lessons.
- Dynamic Steam/external app discovery.
- Practical Windows topology recovery lessons.

### Vibeshine/Vibepollo

Use as research only:

- Inspect targeted driver or HDR changes.
- Inspect any useful decoder/client capability handling.
- Avoid inheriting broad settings complexity.

### ApolloDisplayRescue

Use as a seed for:

- WPF display/window enumeration.
- Move/close/terminate actions.
- Physical primary restore.
- UAC elevation support.

## Testing Without The Phone

The project must be designed so most tests do not require the real Z Fold 7.

### Client Lab Web App And Fake Endpoint Simulator

Build a local Client Lab web app and a CLI simulator that behave like the APK control plane. The web app is for interactive testing, screenshots, and browser-driven flows. The CLI simulator is for deterministic automated tests and scripted reproductions.

Both simulator surfaces must support:

- `hello`.
- Fetch profile.
- Patch allowed client profile fields.
- Submit capabilities.
- Submit telemetry samples.
- Request session plan.
- Request launch.
- Request disconnect.
- Request reconnect.
- Request quit.
- Request emergency restore.

It must use recorded profiles, for example:

```json
{
  "clientId": "z-fold-7",
  "name": "Z Fold 7",
  "screenModes": [
    {"width": 2560, "height": 1600, "refreshHz": 120},
    {"width": 2560, "height": 1600, "refreshHz": 60},
    {"width": 1920, "height": 1200, "refreshHz": 60}
  ],
  "decoders": {
    "h264": true,
    "hevc": true,
    "av1": true,
    "hdr10": true
  }
}
```

The simulator must be runnable from CLI, from tests, and through the Client Lab web app. It must be able to replay realistic sequences:

- Connect with good LAN telemetry.
- Connect with poor network telemetry.
- Change profile before launch.
- Disconnect and reconnect.
- Quit session while app is still running.
- Emergency restore.

### Fake Backends

The orchestrator must depend on interfaces, not concrete Windows side effects.

Important fake backends:

- Fake display backend.
- Fake streaming backend.
- Fake game library provider.
- Fake process/window tracker.
- Fake telemetry source.

This allows deterministic tests for:

- Planner decisions.
- Profile validation.
- Display lifecycle policy.
- Session cleanup rules.
- Recovery action sequencing.
- Game library normalization.

### Windows Display Integration Tests

A smaller set of tests must use the real Windows display backend and SudoVDA, but still not require the phone.

These tests can verify:

- Create virtual display for a fake client.
- Apply `2560x1600@120`.
- Prevent mirror mode.
- Make virtual display primary.
- Restore physical primary.
- Remove virtual display.
- Detect/report HDR capability truthfully.

These tests must be manually runnable and clearly separated from fast unit tests.

### Streaming Backend Contract Tests

The first streaming backend can be tested with a loopback or fake consumer before the APK exists.

Contract assertions:

- Given a session plan, backend receives the expected codec/FPS/bitrate/display id.
- Backend reports started/stopped state.
- Backend can be stopped independently of display cleanup.
- Backend errors are surfaced to the orchestrator without leaving topology unmanaged.

### APK Tests Without Physical Phone

Before using the real phone:

- JVM tests for profile patching logic.
- Android emulator tests for UI/profile editing if useful.
- Contract tests using recorded server responses.
- Decoder/input behavior can be deferred until real-device testing.

The APK must not be required for:

- Planner correctness.
- Game library correctness.
- Display lifecycle correctness.
- Recovery correctness.
- Profile persistence correctness.

### Network Testing Without Phone

Version 1 does not need real network adaptation to prove the architecture. The simulator can submit telemetry profiles:

- Excellent LAN.
- Congested LAN.
- High RTT.
- Packet loss.
- Low bitrate cap.
- Thermal/battery constrained endpoint.

The planner must produce different initial bitrate/codec/fps recommendations and explain the reason.

### Acceptance Gates

Before real phone testing:

- Fake Z Fold 7 can register and fetch profile.
- Fake Z Fold 7 can patch allowed client settings.
- Server rejects global-setting edits from fake APK.
- Server computes a complete session plan before launch.
- Planner preserves `2560x1600` intent and does not silently choose `2560x1440`.
- Planner exposes 120 FPS/refresh decisions when requested and supported.
- Fake display backend verifies correct virtual display lifecycle.
- Fake display backend verifies disconnect alone does not tear down the display.
- Fake display backend verifies display removal requires client inactive **AND** no owned work remains.
- Fake display backend verifies mirror mode and physical-display fallback are not silently selected.
- Real Windows display backend can create and restore a virtual display without a stream.
- Real Windows display backend verifies physical primary restore with reconciliation, not a one-shot best-effort call.
- Real Windows display backend reports HDR capability truthfully.
- Game library scan resolves Steam, Steam shortcuts, Heroic, and Hydra entries into one normalized collection.
- Recovery actions work from WPF or a local test client.

Real phone testing must be reserved for:

- Decoder compatibility.
- Input/touch behavior.
- Actual stream quality.
- Real telemetry quality.
- Human experience.

## Implementation Milestones

### Milestone 0: Source Audit And Extraction Map

Audit Sunshine, Apollo, Vibeshine, Vibepollo, and current rescue helper code. Decide which modules are copied, wrapped, rewritten, or ignored.

Deliverables:

- New public `Darkaxt/beacon-stream` repository created with `gh`.
- Local working tree connected to the GitHub remote before substantial implementation.
- First pushed checkpoint after the initial scaffold.
- GPL-3.0 license unless the extraction map proves no GPL source will be copied or adapted.
- Initial monorepo structure and README.
- Extraction map.
- License notes.
- Backend interface draft.
- Known risky areas.

### Milestone 1: Orchestrator Skeleton

Build the service core with no real streaming.

Deliverables:

- Client registry.
- Client profile store.
- Client Lab web app.
- CLI fake endpoint simulator.
- Session planner.
- Fake display/stream/game/process backends.
- Unit tests for planning and profile validation.
- Tests that prove the phone is not required for profile, planner, lifecycle, and recovery policy validation.

### Milestone 2: Display Lifecycle

Integrate real Windows virtual display backend.

Deliverables:

- Create per-client virtual display.
- Apply client mode.
- Make primary for session.
- Restore physical primary.
- Remove display under server-owned lifecycle rules.
- Manual recovery actions.

### Milestone 3: Game Collection

Build normalized game library.

Deliverables:

- Steam official apps.
- Steam non-Steam shortcuts with correct `rungameid`.
- Heroic.
- Hydra.
- SteamGridDB covers.
- Generated fallback covers.
- Launch intent model.

### Milestone 4: WPF Cockpit

Build the local management UI.

Deliverables:

- Clients view.
- Session/display view.
- Game library view.
- Planner decision view.
- Recovery actions.
- Logs/diagnostics.

### Milestone 5: Streaming Backend Integration

Wrap a proven streaming backend behind the orchestrator.

Deliverables:

- Start stream from session plan.
- Stop stream without losing display ownership.
- Report backend state.
- Surface errors.
- Keep enough Moonlight/GameStream compatibility if useful.

### Milestone 6: Thin APK

Build the Android client.

Deliverables:

- Pair/register.
- Fetch/edit own client profile.
- Report capabilities/telemetry.
- Show game collection from server.
- Request plan and launch.
- Stream/decode/input.
- Emergency restore command.

## Key Design Decisions

- Project name is Beacon Stream.
- Repository target is a new public `Darkaxt/beacon-stream` repo.
- The remote repository is created with `gh` at the start of Milestone 0.
- Work is synced to GitHub after every validated checkpoint to survive context compaction.
- License defaults to GPL-3.0 when Sunshine/Apollo-family source is reused.
- The system is personal-use first.
- Server owns desired state.
- APK edits only its own basic server-side client profile in version 1.
- APK stores only local interaction/UI settings.
- Virtual desktop behavior is always server-owned.
- Version 1 has no per-game virtual desktop overrides.
- Game library is first-class from the beginning.
- Testing must be possible mostly through fake endpoint and fake backends.
- Real phone testing must be a final confirmation path, not the main development loop.

## Risks

- Streaming backend extraction may be more coupled than expected.
- Native capture/encode/input pieces may resist clean isolation.
- SudoVDA/HDR behavior may still be limited by driver capability, Windows Advanced Color exposure, capture format, encoder format, protocol metadata, or Android decoder/display support.
- Windows display topology can behave differently under real user sessions than in tests.
- Android client still needs real-device validation for decoder/input quality.
- If reused source is published, licenses must be respected carefully.

## Weekend GO Criteria

This project is worth starting if the accepted first target is:

- Beacon Stream as a new public repository.
- Remote repository created through `gh` before substantial implementation.
- Validated checkpoints synced to GitHub throughout the work.
- GPL-3.0-compatible source strategy before copying any upstream code.
- Personal-use only.
- One primary endpoint first: Z Fold 7.
- Server-authoritative control plane first.
- Client Lab web app and fake endpoint simulator required from day one.
- WPF cockpit and thin APK follow after the orchestrator is testable.
- Existing projects are treated as lego pieces, not as architecture constraints.

If those are acceptable, the next artifact is an implementation plan for Milestone 0 and Milestone 1 only.
