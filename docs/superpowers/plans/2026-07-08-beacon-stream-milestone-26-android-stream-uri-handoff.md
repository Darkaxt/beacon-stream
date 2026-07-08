# Android Stream URI Handoff Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the thin Android APK consume the server-provided stream connection descriptor by launching `stream.connection.launchUri` after a successful server launch response.

**Architecture:** Keep streaming policy server-owned. Android only extracts the already-computed `launchUri` from the launch response and delegates it to an injectable launcher. JVM tests use a recording launcher; the real Activity uses an Android `ACTION_VIEW` intent. Missing launch URI is non-fatal and does not invent local stream policy.

**Tech Stack:** Java Android app, Gson, Android `Intent.ACTION_VIEW`, JUnit 4 JVM tests, existing Gradle Android project.

---

## Requirements Covered

- `REQ-CTRL-009`: The client consumes the server-computed stream connection data instead of reinterpreting policy locally.
- `REQ-STREAM-005`: Connection details are exposed by the backend/server boundary and consumed by clients.
- `REQ-TEST-010`: Phone testing remains final confirmation; the handoff is covered by JVM tests with an injectable launcher.
- Milestone 17 follow-up: server already emits `stream.connection.launchUri`; Android raw responses carried it but did not act on it.

## File Map

- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/StreamConnectionLauncher.java`
  - Small interface with `launch(String launchUri)`.
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidIntentStreamConnectionLauncher.java`
  - Android implementation using `Intent.ACTION_VIEW`.
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/StreamConnectionLaunchUri.java`
  - Gson-backed extractor for `stream.connection.launchUri`.
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`
  - Accepts an injectable launcher and calls it only after successful launch responses containing a non-empty URI.
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
  - Wires the real intent launcher.
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconViewModelTest.java`
  - Adds JVM tests for launch handoff, failure suppression, and missing-URI suppression.
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/StreamConnectionLaunchUriTest.java`
  - Adds focused extractor tests.
- Update: `README.md`
  - Documents that Android delegates the server connection URI and still does not implement native decode/input yet.

## Task 1: Write Failing Android Handoff Tests

**Files:**
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconViewModelTest.java`
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/StreamConnectionLaunchUriTest.java`

- [ ] **Step 1: Add ViewModel handoff tests**

Add a recording launcher and these tests:

```java
@Test
public void launchDelegatesServerConnectionLaunchUri() throws Exception {
    FakeService service = new FakeService();
    service.next = new BeaconApiClient.BeaconResult(
        200,
        "{\"state\":\"streaming\",\"stream\":{\"connection\":{\"launchUri\":\"moonlight://stream/z-fold-7\"}}}");
    RecordingStreamConnectionLauncher launcher = new RecordingStreamConnectionLauncher();
    BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service, launcher);

    model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

    assertEquals("moonlight://stream/z-fold-7", launcher.launchedUri);
}
```

Also assert no launch on a `503` launch result and no launch when the `connection` object is missing.

- [ ] **Step 2: Add extractor tests**

Assert:

```java
assertEquals(
    "moonlight://stream/z-fold-7",
    StreamConnectionLaunchUri.extract("{\"stream\":{\"connection\":{\"launchUri\":\"moonlight://stream/z-fold-7\"}}}"));
assertEquals("", StreamConnectionLaunchUri.extract("{}"));
assertEquals("", StreamConnectionLaunchUri.extract("{\"stream\":{\"connection\":{\"launchUri\":\"\"}}}"));
```

- [x] **Step 3: Run focused red tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.BeaconViewModelTest --tests dev.beacon.android.StreamConnectionLaunchUriTest
```

Expected before implementation: compile failures for missing `StreamConnectionLauncher`, missing `StreamConnectionLaunchUri`, and missing `BeaconViewModel` constructor overload.

## Task 2: Implement URI Extraction And Launch Handoff

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/StreamConnectionLauncher.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/StreamConnectionLaunchUri.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`

- [x] **Step 1: Add launcher interface**

```java
package dev.beacon.android;

public interface StreamConnectionLauncher {
    void launch(String launchUri);
}
```

- [x] **Step 2: Add Gson extractor**

Use `JsonParser.parseString(responseBody)`, navigate `stream.connection.launchUri`, return the trimmed URI, and return `""` for malformed or missing JSON.

- [x] **Step 3: Add ViewModel injection**

Keep the existing constructor by delegating to a no-op launcher:

```java
public BeaconViewModel(String clientId, String serverUrl, BeaconService service) {
    this(clientId, serverUrl, service, launchUri -> { });
}
```

In `launch`, after `record` and `latestStream`, call the injected launcher only when `result.isSuccess()` and the extracted URI is non-empty.

- [x] **Step 4: Run focused green tests**

Run the same Gradle focused command. Expected: pass.

## Task 3: Wire Android Intent Launcher

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidIntentStreamConnectionLauncher.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`

- [x] **Step 1: Add Android intent launcher**

```java
package dev.beacon.android;

import android.content.Context;
import android.content.Intent;
import android.net.Uri;

public final class AndroidIntentStreamConnectionLauncher implements StreamConnectionLauncher {
    private final Context context;

    public AndroidIntentStreamConnectionLauncher(Context context) {
        this.context = context;
    }

    @Override
    public void launch(String launchUri) {
        Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(launchUri));
        context.startActivity(intent);
    }
}
```

- [x] **Step 2: Wire Activity**

In `createModel`, construct:

```java
return new BeaconViewModel(
    config.clientId(),
    config.serverUrl(),
    new BeaconApiClient(config),
    new AndroidIntentStreamConnectionLauncher(this));
```

- [x] **Step 3: Run Android test/build**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: pass.

## Task 4: Documentation And Full Validation

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-26-android-stream-uri-handoff.md`

- [x] **Step 1: Update README**

Adjust the Android note to say the APK delegates `stream.connection.launchUri` to Android via `ACTION_VIEW` after successful launch, while native decode/input remain future work.

- [x] **Step 2: Run full static and dynamic validation**

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

Expected: all commands pass; the final `rg` returns no matches.

- [ ] **Step 3: Sync**

Commit, push, open a PR, wait for CI, mark ready, merge, and delete the branch when green.
