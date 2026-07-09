# Milestone 79: Android GameStream Endpoint Preflight Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Android APK understand server-provided Sunshine/GameStream endpoint maps well enough to validate the native-stream handoff contract in the emulator before real decoder transport work.

**Architecture:** Add an Android-only GameStream endpoint plan parser over the existing `StreamConnectionDescriptor` endpoint list. `DiagnosticNativeStreamClient` should keep starting the `beacon-test` color-bars stream, but for `gamestream`/`moonlight` endpoint-only descriptors it should validate required endpoint roles and return a precise native-decoder-not-implemented diagnostic instead of the generic missing-launch-URI text.

**Tech Stack:** Java Android unit tests, Gson-backed descriptor parser, existing Beacon Android native stream boundary.

---

## Requirements

- `REQ-CTRL-009`: the APK consumes server-owned stream connection descriptors without reinterpreting server display policy.
- `REQ-CTRL-013`: endpoint-only descriptors produce explicit client behavior or diagnostics.
- `REQ-NET-006`: Sunshine/GameStream-compatible endpoint metadata can be provided as server-owned endpoint maps.
- `REQ-REC-010`: wrapper/client handoff mistakes are visible before phone testing.
- `REQ-TEST-001`: validation should not require the real phone.
- `REQ-TEST-010`: real phone testing remains final confirmation for decoder compatibility and actual stream quality.
- `REQ-SYNC-006`: docs, implementation, and validation status stay aligned.

## Non-Goals

- No Moonlight/GameStream protocol implementation.
- No RTP/RTSP transport.
- No hardware decoder integration.
- No HDR rendering.
- No controller protocol.
- No native touch protocol.

## Files

- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamEndpointPlan.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/DiagnosticNativeStreamClient.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamEndpointPlanTest.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/DiagnosticNativeStreamClientTest.java`
- Modify: `README.md`

## Tasks

### Task 1: RED Android endpoint-plan tests

- [x] **Step 1: Write failing parser tests**

Add `GameStreamEndpointPlanTest` with:

```java
@Test
public void validPlanCollectsRequiredEndpointsCaseInsensitively() {
    StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
        "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
            "{\"role\":\"RTSP\",\"uri\":\"rtsp://127.0.0.1:48010\"}," +
            "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
            "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
            "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");

    GameStreamEndpointPlan plan = GameStreamEndpointPlan.from(descriptor);

    assertTrue(plan.supportedProtocol());
    assertTrue(plan.complete());
    assertEquals("", plan.missingRequiredRoles());
    assertEquals("rtsp=rtsp://127.0.0.1:48010, video=udp://127.0.0.1:47998, control=tcp://127.0.0.1:47999, audio=udp://127.0.0.1:48000", plan.requiredEndpointSummary());
}
```

Also add tests for unsupported protocols and missing required roles.

- [x] **Step 2: Write failing native-client diagnostic tests**

Extend `DiagnosticNativeStreamClientTest` so endpoint-only `gamestream` descriptors with all required roles return:

```text
GameStream endpoint map is complete, but native GameStream decode is not implemented yet. protocol=gamestream endpoints=...
```

and descriptors missing required roles return:

```text
GameStream endpoint map is incomplete. Missing required endpoints: video, control, audio. protocol=gamestream endpoints=...
```

- [x] **Step 3: Run RED tests**

Run:

```powershell
gradle --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.GameStreamEndpointPlanTest --tests dev.beacon.android.DiagnosticNativeStreamClientTest
```

Expected: compile failure because `GameStreamEndpointPlan` does not exist, or diagnostic assertions fail.

### Task 2: GREEN parser and diagnostics

- [x] **Step 1: Implement `GameStreamEndpointPlan`**

Create a final class that:

- supports protocols `gamestream` and `moonlight`;
- treats endpoint roles case-insensitively;
- requires roles `rtsp`, `video`, `control`, and `audio`;
- preserves endpoint URI values exactly as provided;
- exposes `supportedProtocol()`, `complete()`, `missingRequiredRoles()`, `requiredEndpointSummary()`, and `diagnosticEndpointSummary()`.

- [x] **Step 2: Update `DiagnosticNativeStreamClient`**

Keep existing `beacon-test` behavior. For endpoint-only `gamestream`/`moonlight` descriptors:

- return unsupported with the incomplete endpoint diagnostic when required roles are missing;
- return unsupported with the complete-but-decoder-missing diagnostic when all required roles exist;
- keep the generic missing-launch-URI diagnostic for unrelated protocols.

- [x] **Step 3: Run GREEN tests**

Run the Task 1 Gradle command again. Expected: PASS.

### Task 3: Docs and validation

- [x] **Step 1: Update README**

Document that Android now validates GameStream endpoint maps for emulator/no-phone handoff diagnostics, but still does not implement native decode.

- [x] **Step 2: Validate static and dynamic checks**

Run:

```powershell
git diff --check
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
gradle --no-daemon -p src\Beacon.Android test assembleDebug
dotnet test Beacon.slnx --no-build
```

Expected: all commands pass; `rg` should return no matches.

### Task 4: Sync

- [ ] **Step 1: Commit**

Commit all milestone files with:

```powershell
git add README.md docs\superpowers\plans\2026-07-09-beacon-stream-milestone-79-android-gamestream-endpoint-preflight.md src\Beacon.Android\app\src\main\java\dev\beacon\android\GameStreamEndpointPlan.java src\Beacon.Android\app\src\main\java\dev\beacon\android\DiagnosticNativeStreamClient.java src\Beacon.Android\app\src\test\java\dev\beacon\android\GameStreamEndpointPlanTest.java src\Beacon.Android\app\src\test\java\dev\beacon\android\DiagnosticNativeStreamClientTest.java
git commit -m "Validate Android GameStream endpoint maps"
```

- [ ] **Step 2: Push and PR**

Push `codex/milestone-79-android-gamestream-endpoint-preflight`, open a PR, watch CI, and merge when green.
