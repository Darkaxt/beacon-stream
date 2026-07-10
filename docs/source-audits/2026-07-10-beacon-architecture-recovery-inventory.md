# Beacon Architecture Recovery Inventory

Date: 2026-07-10

Baseline: `0b045e5` (`Redesign Beacon around one native streaming path (#121)`)

Authority: `docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md`

## Purpose

This inventory defines the first deletion boundary for Beacon architecture recovery. It prevents two opposite failures:

1. retaining compatibility code because it already has tests; and
2. deleting sound control-plane, display, game, recovery, benchmark-input, or decoder primitives merely because they are located near obsolete streaming code.

The classifications below apply to Recovery Gates 0–2. StreamWorker source selection happens only after these gates leave a clean Beacon-owned boundary.

## Current Debt Shape

- `src/Beacon.Platform.Windows/Streaming`: 5 tracked files, approximately 1,347 lines.
- `ExternalProcessStreamingBackend.cs`: approximately 1,054 lines.
- `src/Beacon.StreamingProbe`: 5 tracked files, approximately 476 lines.
- `tests/Beacon.StreamingProbe.Tests`: 3 tracked files, approximately 345 lines.
- `src/Beacon.Android/app/src/main/java/dev/beacon/android`: 104 tracked Java files, approximately 6,428 lines.
- `src/Beacon.Android/streaming-moonlight`: 16 tracked wrapper/build files plus two large Git submodules.
- Core exposes `MoonlightNativeSessionDescriptor`, launch URIs, endpoint maps, wrapper health, and runtime descriptor state.
- Server registration exposes `fake`, `external-process`, and `beacon-test` production selections plus wrapper-specific configuration.
- Android contains Java GameStream RTSP/RTP, Moonlight JNI, encoded-video HTTP, test-pattern, intent fallback, and router paths in parallel.

## Keep

These components align with the approved architecture and remain protected by tests.

### Beacon Core

- `Clients/*`: client identity, profile persistence, capability facts, and telemetry facts.
- `Displays/*`: per-client leases, the inactive **AND** no-owned-work cleanup rule, and fake display testing.
- `Games/*`: Steam, non-Steam shortcuts, Heroic, Hydra, manual entries, SteamGridDB, and fallback covers.
- `Input/ClientInput.cs`: server-owned typed input facts.
- `Recovery/*`: recovery contract and deterministic fake.
- `Sessions/*`: planner, immutable plan, process/window ownership, and activity inspection contract.
- `Diagnostics/*`: operational journal.

### Windows Platform

- `Displays/*`: SudoVDA and DisplayConfig lifecycle.
- `Games/*`: Windows application launch and library integration.
- `Input/*`: Win32 input injection boundary.
- `Recovery/*`: physical restore and window/process recovery.
- `Sessions/*`: process/window activity inspection.

### Server And Cockpit

- Client registration/profile persistence.
- Planning, beacon lease preparation, launch ownership, disconnect/quit cleanup, and recovery sequencing.
- Game catalog and artwork endpoints.
- Generic diagnostics and display/input health.
- WPF clients, sessions, games, plans, recovery, and logs.

### Android

- `BeaconApiClient`, `BeaconHttpTransport`, and `HttpUrlConnectionBeaconTransport`.
- `BeaconGameCatalog` and game selection.
- `BeaconClientConfig`, JSON, and result models.
- Device capability and telemetry probes.
- Local settings, storage, form, and UI state.
- Touch input mapping and existing HTTP input forwarding until StreamCore replaces the transport boundary.
- Main-thread dispatch and lifecycle session ownership.

## Adapt

These are generic primitives with valuable tests, but their names or current owners may change when StreamCore is introduced.

- `AndroidMediaCodecCatalog` and `AndroidCodecDescriptor`: retain for capability inventory and hardware benchmark candidate selection.
- `AndroidMediaCodecFactory`, `EncodedVideoCodec`, `EncodedVideoCodecFactory`, and `SurfaceEncodedVideoDecoder`: retain as the current Surface/MediaCodec adapter boundary.
- `EncodedVideoDecodeRequest`, `EncodedVideoDecodeResult`, `EncodedVideoSample`, `EncodedVideoDecoder`, `EncodedVideoSurfaceProvider`, and `AndroidSurfaceViewProvider`: retain only as decoder/presentation primitives; remove HTTP/test/GameStream ownership.
- `AnnexBAccessUnitSplitter`: retain only if the StreamCore source audit confirms Java-side access-unit splitting remains necessary; otherwise delete before Gate 3 implementation.
- `NativeStreamPresentation`: rename under the Beacon StreamCore contract or delete if native rendering no longer needs a Java presentation record.
- `IStreamingBackend`: retain the service boundary name during Gates 0–2, but remove protocol, wrapper, endpoint, launch-URI, and native-session concepts. A single production StreamWorker implementation and one fake test implementation are the only permitted implementations after Gate 3.
- `StreamingSessionState`: retain session identity and actual media facts; remove transport-selection and connection-handoff fields.

## Delete: Core And Diagnostic Streaming

- `src/Beacon.Core/Streaming/MoonlightNativeSessionDescriptor.cs`
- `tests/Beacon.Core.Tests/Streaming/MoonlightNativeSessionDescriptorTests.cs`
- `src/Beacon.Core/Streaming/BeaconTestStreamingBackend.cs`
- `tests/Beacon.Core.Tests/Streaming/BeaconTestStreamingBackendTests.cs`
- `StreamingConnectionDescriptor`
- `StreamingEndpointDescriptor`
- `StreamingClientSessionSnapshot`
- `IStreamingBackend.GetNativeSessionAsync`
- `IStreamingBackend.GetClientSessionAsync`
- wrapper/executable/manifest/protocol/launch-URI/endpoint properties from `StreamingBackendHealth`

## Delete: External Server And Wrapper Path

- `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`
- `src/Beacon.Platform.Windows/Streaming/SunshineEndpointProfile.cs`
- `src/Beacon.Platform.Windows/Streaming/WindowsExternalStreamingManifestReader.cs`
- `src/Beacon.Platform.Windows/Streaming/WindowsExternalStreamingProcessRunner.cs`
- `src/Beacon.Platform.Windows/Streaming/WindowsExternalStreamingSessionDescriptorStore.cs`
- `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`
- `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalStreamingManifestContractTests.cs`
- `src/Beacon.StreamingProbe/*`
- `tests/Beacon.StreamingProbe.Tests/*`
- both StreamingProbe projects from `Beacon.slnx`
- the StreamingProbe project reference from `Beacon.Server.Tests.csproj`
- `BeaconStreamingBackendMode`
- external/backend-selection configuration and environment variables in `BeaconServiceRegistration`
- external and test-stream sections in server appsettings
- diagnostic H.264 assets and `StreamAssetEndpoints`

Windows host mode must fail closed with an explicit architecture-recovery backend until StreamWorker exists. It must never register the fake backend as a production substitute.

## Delete: Public API And Cockpit Compatibility Shape

- `nativeSession` in launch and stream responses.
- launch URI, endpoint map, protocol, wrapper child, manifest, and executable health fields.
- WPF formatting and models for those fields.
- tests that require these removed fields.

Launch, stop, ownership, display, and recovery behavior remain. During recovery, a Windows launch fails stream preflight explicitly until StreamWorker is implemented; fake-host integration tests continue through the protocol-neutral fake backend.

## Delete: Android Compatibility Paths

Delete the following families from production and tests:

- `GameStream*`
- `Moonlight*` and `JniMoonlight*`
- `Rtsp*`
- `Rtp*`
- `H264RtpSampleProvider` and `H264ParameterSets`
- `AndroidIntentStreamConnectionLauncher`
- `DispatchingStreamConnectionLauncher`
- `StreamConnectionLauncher`
- `StreamConnectionLaunchUri`
- the compatibility-shaped `StreamConnectionDescriptor`
- `NativeStreamClientRouter` and protocol-client routing
- `BeaconTestNativeStreamClient`, test-pattern view/palette, and diagnostic protocol client
- `EncodedVideoNativeStreamClient`, HTTP encoded-video providers, encoded-video stream plan, and Beacon Annex-B HTTP envelope
- tests whose only purpose is one of those paths
- `src/Beacon.Android/streaming-moonlight`
- both Git submodule entries
- Gradle inclusion and app dependency for `:streaming-moonlight`
- Moonlight instrumentation tests
- NDK/CMake CI installation that becomes unnecessary after module removal

`BeaconViewModel` remains responsible for catalog selection, preflight request, launch request, stop, disconnect, quit, benchmark calls when added, and factual status. It must not parse or launch a media connection until the one Beacon StreamCore contract is introduced.

## Delete Or Rewrite: Documentation

Delete compatibility contracts and source audits after their code is removed:

- `docs/external-streaming-wrapper-manifest.md`
- `docs/examples/external-streaming-runtime-session.example.json`
- `docs/source-audits/2026-07-10-moonlight-native-core.md`

Rewrite `README.md` as current product/build/validation documentation. Historical milestone narratives remain available in Git history and must not dominate the current README.

Update `docs/extraction-map.md` to record deleted compatibility paths and preserve provenance only for retained/adapted primitives.

Historical implementation plans that specify wrappers, GameStream, Moonlight, RTSP/RTP, runtime descriptors, or alternate client routes are deleted once code removal lands. Plans for preserved display, games, recovery, profiles, telemetry facts, local settings, and generic input remain.

## Protected Behavior Matrix

The following evidence must remain green through every deletion slice:

- Session planner preserves `2560x1600`, explicit 120 FPS, HDR reasons, and benchmark inputs.
- Display leases remain per client and enforce inactive **AND** no-owned-work removal.
- Physical-primary restore and recovery sequencing remain covered.
- Steam/non-Steam/Heroic/Hydra/manual catalog and artwork tests remain.
- Process, child-process, and top-level-window ownership remains.
- Client registration, persistence, catalog selection, launch ownership, disconnect, quit, and emergency restore APIs remain.
- Client Lab and fake endpoint continue to exercise the control plane.
- Android game catalog, capability facts, telemetry facts, local settings, touch mapping, and HTTP control-plane tests remain.
- Generic MediaCodec configure/start/queue/release tests remain until the StreamCore source audit either adopts or replaces that boundary.

## Gates 0–2 Exit State

- No upstream protocol type exists in Beacon Core.
- No external-process or wrapper streaming implementation exists.
- No backend selection exists in production configuration.
- No public API or WPF model exposes compatibility handoff fields.
- Android has no GameStream, Moonlight, RTSP, RTP, launch-intent, HTTP sample, test-pattern, router, or fallback streaming path.
- Android has no native streaming submodule or submodule checkout requirement.
- Fake-host control-plane tests remain usable.
- Windows host streaming fails closed with one explicit StreamWorker-not-implemented diagnostic.
- The repository is ready for a focused upstream primitive audit and a separate Gate 3 StreamWorker/StreamCore plan.
