# R2 Controller Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver one physical Android gamepad through Beacon's authenticated input path as a
session-owned virtual Xbox 360 controller on Windows.

**Architecture:** Android maps one physical gamepad to stable Beacon control identifiers and sends
only controller input batches through the existing StreamCore and StreamWorker transport. The Windows
input sink validates the session target, applies complete state to a lazily connected ViGEm Xbox 360
target, retains it across transport reconnects, and releases it on explicit session quit/stop or server
shutdown. ViGEm is an implementation primitive, not a compatibility protocol or lifecycle owner.

**Tech Stack:** Android Java 17 input APIs, JNI/C++ protobuf mapping, .NET 10, Nefarius ViGEm Client
1.21.256, xUnit, JUnit, existing Gate 5 production acceptance.

---

## Scope Boundary

- One controller, index `0`, represented as an Xbox 360 device.
- Buttons: d-pad, A/B/X/Y, shoulders, thumb clicks, Start, Back, and Guide.
- Axes: left/right X/Y and analog triggers.
- No controller overlay, rumble, motion, touchpad, remapping UI, multi-controller policy, or DS4 mode.
- Disconnect/reconnect retains the virtual controller; quit, admin stop, and server disposal remove it.
- No polling timeout or inactivity teardown.

## Stable Control Contract

`control_id` values remain unsigned integers in `stream_control.proto` and use this R2 table:

| ID | Control | Value |
| ---: | --- | --- |
| 1-15 | Up, Down, Left, Right, Start, Back, LeftThumb, RightThumb, LeftShoulder, RightShoulder, Guide, A, B, X, Y | `0` or `1` |
| 16-17 | LeftTrigger, RightTrigger | `0..255` |
| 18-21 | LeftX, LeftY, RightX, RightY | `-32768..32767` |

Android Y axes are inverted before transport so positive values match XInput's upward direction.

### Task 1: Extend the Java-to-native controller contract

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconApiClient.java`
- Modify: `src/Beacon.Android/app/src/main/cpp/streamcore/jni_bridge.cpp`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconApiClientTest.java`
- Test: `src/Beacon.Android/app/src/androidTest/java/dev/beacon/android/BeaconStreamCoreInstrumentationTest.java`

- [ ] Add a failing JUnit test that constructs `InputBatch.controller(7, 0, 12, 1)` and proves its
  sequence, controller index, control ID, and value.
- [ ] Run the single Java test and verify it fails because controller fields/factories are absent.
- [ ] Add nullable `controllerIndex`, `controlId`, and `value` fields plus controller event/batch
  factories. Reject negative sequence/index/ID values and values outside signed 16-bit range except
  trigger values, which are further validated by the Windows sink.
- [ ] Add a failing instrumentation case that sends a controller event through normal StreamCore and
  expects the hosted peer to observe the protobuf controller body.
- [ ] Extend `parse_input` to emit `InputEvent.controller` using the three Java fields, then run the
  focused unit/native Android checks green.

### Task 2: Map Android physical gamepad events

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidGamepadMapper.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/AndroidGamepadMapperTest.java`

- [ ] Write failing plain-Java tests for A press/release, d-pad, both sticks, both triggers, Y-axis
  inversion, dead-zone centering, clamping, and rejection of non-gamepad key codes.
- [ ] Run the mapper test and verify the class is absent.
- [ ] Implement a stateless mapper. Convert normalized stick values to signed 16-bit and triggers to
  bytes, applying the Android motion range's flat value before scaling.
- [ ] Add monotonic input sequencing and `hasActiveStream()` to `BeaconViewModel` without creating a
  StreamCore merely to answer the query.
- [ ] Override activity key and generic-motion dispatch. Consume mapped gamepad events only while a
  production stream is active; otherwise delegate unchanged to Android.
- [ ] Run the focused Android unit suite green.

### Task 3: Add a session-owned Windows virtual controller

**Files:**
- Modify: `src/Beacon.Platform.Windows/Beacon.Platform.Windows.csproj`
- Create: `src/Beacon.Platform.Windows/Input/WindowsVirtualController.cs`
- Modify: `src/Beacon.Platform.Windows/Input/WindowsClientInputSink.cs`
- Test: `tests/Beacon.Platform.Windows.Tests/Input/WindowsVirtualControllerTests.cs`
- Test: `tests/Beacon.Platform.Windows.Tests/Input/WindowsClientInputSinkTests.cs`

- [ ] Add failing xUnit tests for control mapping, range rejection, batched single-report submission,
  one target per session/controller, reconnect retention, and idempotent release.
- [ ] Run the focused Windows tests and verify the missing interface/implementation failures.
- [ ] Add `Nefarius.ViGEm.Client` `1.21.256`. Hide it behind `IWindowsVirtualControllerApi` so tests use
  a deterministic fake and no test creates a system controller.
- [ ] Implement one lazily connected Xbox 360 target for controller index `0`. Set
  `AutoSubmitReport=false`, apply every event in order, and call `SubmitReport()` once per batch.
- [ ] Route controller batches through this API after the existing display/session-target validation.
  Continue routing pointer/keyboard commands through SendInput; reject mixed invalid batches without
  partially applying either sink.
- [ ] Report controller availability and an actionable `vigem-unavailable` result without crashing the
  Server when the driver is absent.
- [ ] Run the focused Windows tests green.

### Task 4: Bind controller cleanup to session lifecycle

**Files:**
- Modify: `src/Beacon.Core/Input/ClientInput.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`
- Test: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Test: `tests/Beacon.Server.Tests/AdminApiTests.cs`

- [ ] Add failing API tests proving disconnect does not release a controller, while quit and admin
  stream stop release the exact session controller once.
- [ ] Run the focused Server tests and verify release is not yet invoked.
- [ ] Add `IClientInputSessionLifecycle.ReleaseSessionAsync`. Implement it in the Windows input sink and
  register the same singleton for input, health, and lifecycle roles.
- [ ] Invoke release only after successful explicit quit/stop runtime handling. Dispose all remaining
  targets during Server shutdown. Do not bind cleanup to transport disconnect.
- [ ] Run focused Core, Windows, and Server tests green.

### Task 5: Prove the production controller transaction

**Files:**
- Modify: `tests/Beacon.SessionProbe/Program.cs`
- Modify: `tests/Beacon.ProductionAcceptance/Program.cs`
- Modify: `scripts/test-gate5-production-session.ps1`
- Modify: `docs/validation/beacon-release-blocker.md`

- [ ] Add a failing SessionProbe assertion that records an Xbox A transition through XInput.
- [ ] Extend the standard Gate 5 instrumentation action to send controller A down/up through the same
  JNI/StreamCore path used by the APK.
- [ ] Run static format/build plus focused managed, Windows native, and Android checks.
- [ ] Confirm the Windows input desktop is `Default`, then run only
  `scripts/test-gate5-production-session.ps1 -Serial emulator-5554 -ArtifactsReady`.
- [ ] Require retained evidence for moving video, keyboard F12, Xbox A, disconnect/reconnect, explicit
  quit, controller removal, lease removal, and physical-only restore.
- [ ] Independently run `restore-physical`, DisplayProbe status, and HostAgent Control status. Require
  physical primary, mirror disabled, no virtual output, zero leases, and no heartbeat.
- [ ] Commit and push the controller slice before beginning audio.

## Exit Condition

The controller slice is complete only when the normal APK transport produces an Xbox 360 input event
inside the launched Windows session, disconnect/reconnect retains the same controller target, explicit
quit removes it, and the guarded transaction still restores the laptop to physical-only primary
topology. Audio remains the next R2 slice.
