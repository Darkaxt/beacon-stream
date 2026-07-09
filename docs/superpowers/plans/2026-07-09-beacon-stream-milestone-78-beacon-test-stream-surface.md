# Milestone 78: Beacon Test Stream Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deterministic no-phone stream path that lets Beacon Server hand the Android APK an endpoint-only `beacon-test` stream and lets the APK render it in-app.

**Architecture:** Keep the real GameStream/Moonlight path honest and unimplemented. Add a server-side `BeaconTestStreamingBackend` that publishes `protocol=beacon-test` with a `video=beacon-test://pattern/color-bars` endpoint and no launch URI, then add an Android native stream presentation model plus a simple custom `View` for rendering that test pattern. This proves the APK can consume server-owned endpoint maps inside the app without claiming real decoder support.

**Tech Stack:** .NET 10 Core/Server tests, Java Android JVM tests, Android custom `View`, ADB emulator smoke.

---

## Requirements

- `REQ-CTRL-008`: the server computes and owns the launch/stream handoff before the client starts streaming.
- `REQ-CTRL-009`: the APK consumes the server-provided connection descriptor instead of reinterpreting policy.
- `REQ-CTRL-013`: endpoint-only descriptors must produce explicit client behavior or diagnostics.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-TEST-010`: real phone testing remains final confirmation for real decoder quality.
- `REQ-SYNC-006`: docs, implementation, and validation status stay aligned.

## Non-Goals

- No Moonlight/GameStream protocol implementation.
- No hardware video decode.
- No HDR rendering.
- No controller protocol.
- No native Windows touch injection.

## Files

- Create: `src/Beacon.Core/Streaming/BeaconTestStreamingBackend.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconStreamingBackendMode.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconHostOptions.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Test: `tests/Beacon.Core.Tests/Streaming/BeaconTestStreamingBackendTests.cs`
- Test: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`
- Test: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/NativeStreamPresentation.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconTestPatternView.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/NativeStreamStartResult.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/DiagnosticNativeStreamClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/StreamConnectionDescriptor.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/DiagnosticNativeStreamClientTest.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconViewModelTest.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/StreamConnectionLaunchUriTest.java`
- Modify: `README.md`

## Tasks

### Task 1: Server beacon-test streaming backend

- [x] **Step 1: Write failing Core tests**

Add `BeaconTestStreamingBackendTests` proving `StartAsync` returns a running session with `protocol=beacon-test`, no `launchUri`, endpoint `video=beacon-test://pattern/color-bars`, and plan metadata.

- [x] **Step 2: Run Core tests and confirm RED**

Run `dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter BeaconTestStreamingBackendTests`. Expected: compile failure because `BeaconTestStreamingBackend` does not exist.

- [x] **Step 3: Implement the backend**

Create `BeaconTestStreamingBackend` behind `IStreamingBackend`. It must be deterministic, ready by default, keep sessions until stopped, and expose health as `backend=beacon-test`, `protocol=beacon-test`, endpoint `video=beacon-test://pattern/color-bars`.

- [x] **Step 4: Run Core tests and confirm GREEN**

Run the same filtered Core test command. Expected: PASS.

### Task 2: Server selection and API handoff

- [x] **Step 1: Write failing registration/API tests**

Add tests proving `Beacon:Streaming:Backend=beacon-test` selects `BeaconTestStreamingBackend`, reports mode name `beacon-test`, and `/clients/z-fold-7/launch` returns endpoint-only `beacon-test` connection data.

- [x] **Step 2: Run server tests and confirm RED**

Run `dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "BeaconServiceRegistrationTests|ClientApiTests"`. Expected: fail until the new streaming mode is wired.

- [x] **Step 3: Wire hosting mode**

Add `BeaconStreamingBackendMode.BeaconTest`, resolve `beacon-test`, register `BeaconTestStreamingBackend`, and update clear invalid-mode guidance.

- [x] **Step 4: Run server tests and confirm GREEN**

Run the same filtered server test command. Expected: PASS.

### Task 3: Android native presentation model

- [x] **Step 1: Write failing Android JVM tests**

Extend Android tests so `DiagnosticNativeStreamClient` accepts only a `beacon-test://pattern/color-bars` video endpoint, returns a `NativeStreamPresentation` with `kind=color-bars`, and `BeaconViewModel` exposes/clears that presentation on launch/stop/switch.

- [x] **Step 2: Run Android JVM tests and confirm RED**

Run Gradle `testDebugUnitTest --tests dev.beacon.android.DiagnosticNativeStreamClientTest --tests dev.beacon.android.BeaconViewModelTest --tests dev.beacon.android.StreamConnectionLaunchUriTest`. Expected: compile/test failure because the presentation model is missing.

- [x] **Step 3: Implement presentation state**

Add `NativeStreamPresentation`, extend `NativeStreamStartResult`, expose connection endpoints safely from `StreamConnectionDescriptor`, update `DiagnosticNativeStreamClient`, and update `BeaconViewModel`.

- [x] **Step 4: Run Android JVM tests and confirm GREEN**

Run the same filtered Gradle command. Expected: PASS.

### Task 4: Android in-app test-pattern surface

- [x] **Step 1: Add the custom rendering view**

Create `BeaconTestPatternView`, a custom Android `View` that draws static color bars and a label from `NativeStreamPresentation`. It must be hidden when no native presentation is active.

- [x] **Step 2: Wire Activity visibility**

Add the view above the touch/input surface, update it after actions using `model.latestNativeStreamPresentation()`, and keep theme/application state separate from server display policy.

- [x] **Step 3: Build APK and smoke emulator**

Run Android `test assembleDebug`, install to `emulator-5554` if available, and launch `dev.beacon.android/.BeaconActivity`.

### Task 5: Docs, validation, and sync

- [x] **Step 1: Update README**

Document `BEACON_STREAMING_BACKEND=beacon-test` and the Android in-app test-pattern path as a no-phone diagnostic stream, explicitly separate from real decode.

- [x] **Step 2: Validate static and dynamic checks**

Run diff check, timeout-pattern audit, .NET format/build/test, Android test/assemble, Client Lab checks if server/API response shape affects frontend expectations, display probe status, and emulator smoke when available.

- [ ] **Step 3: Sync**

Commit, push, open PR, verify CI, and merge when green.
