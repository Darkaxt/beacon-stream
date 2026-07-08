# Android Preflight Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the thin Android APK control plane up to parity with the server and simulators by sending expanded capability/telemetry facts before plan and launch, preserving APK profile-edit boundaries, and surfacing the server's plan reason.

**Architecture:** Keep the APK policy-free. Android only gathers/serializes client facts and invokes the server-owned profile, capability, telemetry, plan, launch, and emergency restore endpoints. The server still owns virtual desktop behavior, stream planning, display topology, and recovery policy.

**Tech Stack:** Java Android app, Android Gradle Plugin, Gson, JUnit 4, existing injectable `BeaconHttpTransport` for no-phone tests.

---

## Requirements Covered

- `REQ-CTRL-002`: APK reports identity, facts, capabilities, preferences, telemetry, and user intent.
- `REQ-CTRL-003`: APK can update its own basic server-side profile before launch.
- `REQ-CTRL-009`: APK consumes the server plan instead of reinterpreting topology or stream policy.
- `REQ-CTRL-011`: APK profile patches stay inside the server allowlist.
- `REQ-NET-002`: APK telemetry payload includes RTT, packet loss, Wi-Fi band, decoder load, current screen mode, battery, and thermal hints.
- `REQ-REC-005`: APK emergency restore remains scoped to the owning client endpoint.
- `REQ-TEST-010`: real phone validation remains final confirmation; this milestone uses JVM tests.

## Files

- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconApiClient.java`
  - Extend `ClientCapabilities` with `maxFps`, `lowLatencyDecode`, and `currentScreenMode`.
  - Extend `ClientTelemetry` with `estimatedBandwidthMbps`, `wifiBand`, `batteryPercent`, and `thermalState`.
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`
  - Add `preflight(ProfilePatch, ClientCapabilities, ClientTelemetry)` that calls patch, capabilities, and telemetry in order.
  - Add `preflightAndPlan(...)` and `preflightAndLaunch(...)` helpers so the UI can execute the right order without duplicating policy.
  - Store `latestPlan` as the full server response so plan reasons are visible.
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
  - Add inputs for telemetry profile facts that the current UI does not expose.
  - Make Plan and Launch run preflight first.
  - Show the server response body containing the plan reason.
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconApiClientTest.java`
  - Assert expanded capability and telemetry JSON fields.
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconViewModelTest.java`
  - Assert preflight ordering before plan and launch.
  - Assert launch still does not send display topology decisions.
- Modify: `README.md`
  - Note that the APK now sends the expanded Milestone 15 facts before plan/launch.

## Task 1: Android API Payload Parity

- [x] **Step 1: Write failing API client tests**

Add tests:

```java
@Test
public void capabilitiesSerializeExpandedPlanningFacts() throws Exception {
    FakeTransport transport = new FakeTransport();
    BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);

    client.reportCapabilities(new BeaconApiClient.ClientCapabilities(true, true, true, false, false, 120, true, "2560x1600@120"));

    assertEquals("/clients/z-fold-7/capabilities", transport.path);
    assertTrue(transport.body.contains("\"maxFps\":120"));
    assertTrue(transport.body.contains("\"lowLatencyDecode\":true"));
    assertTrue(transport.body.contains("\"currentScreenMode\":\"2560x1600@120\""));
}

@Test
public void telemetrySerializesExpandedPlanningFacts() throws Exception {
    FakeTransport transport = new FakeTransport();
    BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);

    client.reportTelemetry(new BeaconApiClient.ClientTelemetry(8, 0.0, 20, 120, "wifi-7", 80, "nominal"));

    assertEquals("/clients/z-fold-7/telemetry", transport.path);
    assertTrue(transport.body.contains("\"estimatedBandwidthMbps\":120"));
    assertTrue(transport.body.contains("\"wifiBand\":\"wifi-7\""));
    assertTrue(transport.body.contains("\"batteryPercent\":80"));
    assertTrue(transport.body.contains("\"thermalState\":\"nominal\""));
}
```

- [x] **Step 2: Verify tests fail**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.BeaconApiClientTest
```

Expected: compile failures for missing constructor fields.

- [x] **Step 3: Implement payload fields**

Append constructor parameters to keep call sites obvious. `toJson()` must emit exactly the field names accepted by the server: `maxFps`, `lowLatencyDecode`, `currentScreenMode`, `estimatedBandwidthMbps`, `wifiBand`, `batteryPercent`, and `thermalState`.

- [x] **Step 4: Verify API tests pass**

Run the same filtered Gradle command. Expected: pass.

## Task 2: Preflight Ordering In ViewModel

- [x] **Step 1: Write failing ViewModel tests**

Add tests:

```java
@Test
public void preflightAndPlanPatchesProfileThenReportsFactsThenRequestsPlan() throws Exception {
    FakeService service = new FakeService();
    BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

    model.preflightAndPlan(new BeaconApiClient.ProfilePatch(), defaultCapabilities(), defaultTelemetry(), BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

    assertEquals("patch,capabilities,telemetry,plan", service.actions());
    assertEquals("plan: 200", model.status());
}

@Test
public void preflightAndLaunchPatchesProfileThenReportsFactsThenLaunches() throws Exception {
    FakeService service = new FakeService();
    BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

    model.preflightAndLaunch(new BeaconApiClient.ProfilePatch(), defaultCapabilities(), defaultTelemetry(), BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

    assertEquals("patch,capabilities,telemetry,launch", service.actions());
    assertEquals("launch: 200", model.status());
}
```

Implement helper methods in the fake test class:

```java
private static BeaconApiClient.ClientCapabilities defaultCapabilities() {
    return new BeaconApiClient.ClientCapabilities(true, true, true, false, false, 120, true, "2560x1600@120");
}

private static BeaconApiClient.ClientTelemetry defaultTelemetry() {
    return new BeaconApiClient.ClientTelemetry(8, 0.0, 20, 120, "wifi-7", 80, "nominal");
}
```

- [x] **Step 2: Verify tests fail**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.BeaconViewModelTest
```

Expected: compile failures for missing ViewModel methods and fake action log.

- [x] **Step 3: Implement ViewModel preflight helpers**

`preflight()` must call `patchProfile`, `reportCapabilities`, and `reportTelemetry` in that order. `preflightAndPlan()` then calls `requestPlan()` and stores `latestPlan`. `preflightAndLaunch()` then calls `launch()` and stores `latestStream`. Do not add display topology decisions to the APK.

- [x] **Step 4: Verify ViewModel tests pass**

Run the same filtered Gradle command. Expected: pass.

## Task 3: Activity Wiring And Docs

- [x] **Step 1: Wire Activity Plan and Launch buttons**

Add or compute:

- Capabilities: AV1/HEVC/H.264 true, HDR10 false by default, virtual display HDR false by default, max FPS from refresh input, current screen mode from width/height/refresh.
- Telemetry: excellent-LAN defaults matching Milestone 15: RTT 8, loss 0, decoder load 20, bandwidth 120, Wi-Fi `wifi-7`, battery 80, thermal `nominal`.
- Plan button calls `preflightAndPlan(readPatch(), readCapabilities(), readTelemetry(), readGame())`.
- Launch button calls `preflightAndLaunch(readPatch(), readCapabilities(), readTelemetry(), readGame())`.

- [x] **Step 2: Update README**

Mention that the Android shell now sends expanded capability/telemetry facts before plan/launch and still leaves display behavior policy on the server.

- [x] **Step 3: Run validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
dotnet test Beacon.slnx
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright lint
pnpm --dir tests\Beacon.ClientLab.Playwright test
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
```

Expected: all commands pass, with the known Gradle 9 deprecation warning allowed if the Android command exits successfully.

- [x] **Step 4: Commit and sync**

Run:

```powershell
git add README.md src/Beacon.Android docs/superpowers/plans/2026-07-08-beacon-stream-milestone-16-android-preflight-parity.md
git commit -m "Add Android preflight parity"
git push -u origin codex/milestone-16-android-preflight-parity
gh pr create --draft --base main --head codex/milestone-16-android-preflight-parity --title "Add Android preflight parity" --body "Milestone 16 Android preflight parity implementation."
gh pr checks <pr-number> --watch
gh pr ready <pr-number>
gh pr merge <pr-number> --merge --delete-branch
```

Expected: PR checks pass, PR merges, and local `main` is clean.

## Non-Goals

- No native video decode.
- No input forwarding.
- No real phone-only telemetry dependency.
- No server-global settings from APK.
- No Android display topology policy.
