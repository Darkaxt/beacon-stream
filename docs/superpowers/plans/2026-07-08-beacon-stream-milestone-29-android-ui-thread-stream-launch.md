# Milestone 29 Android UI Thread Stream Launch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure Android stream connection URI handoff is dispatched onto the Activity UI thread before invoking Android `startActivity`.

**Architecture:** Keep `BeaconViewModel` platform-neutral. Wrap the platform launcher in a pure-Java `DispatchingStreamConnectionLauncher` that posts launch work through a small `MainThreadDispatcher`; `BeaconActivity` supplies an Android dispatcher backed by `runOnUiThread`.

**Tech Stack:** Android Java, JVM unit tests, existing `StreamConnectionLauncher` boundary.

---

### Task 1: Dispatch Wrapper

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/MainThreadDispatcher.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/DispatchingStreamConnectionLauncher.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/StreamConnectionLaunchUriTest.java`

- [x] **Step 1: Write the failing JVM tests**

Add tests:

```java
@Test
public void dispatchingLauncherPostsLaunchToDispatcher() {
    RecordingDispatcher dispatcher = new RecordingDispatcher();
    RecordingLauncher inner = new RecordingLauncher();
    DispatchingStreamConnectionLauncher launcher = new DispatchingStreamConnectionLauncher(dispatcher, inner);

    launcher.launch("moonlight://stream/z-fold-7");

    assertEquals("", inner.launchedUri);
    assertEquals(1, dispatcher.pendingCount());

    dispatcher.runPending();

    assertEquals("moonlight://stream/z-fold-7", inner.launchedUri);
}

@Test
public void dispatchingLauncherPreservesLaunchOrder() {
    RecordingDispatcher dispatcher = new RecordingDispatcher();
    RecordingLauncher inner = new RecordingLauncher();
    DispatchingStreamConnectionLauncher launcher = new DispatchingStreamConnectionLauncher(dispatcher, inner);

    launcher.launch("moonlight://one");
    launcher.launch("moonlight://two");
    dispatcher.runPending();

    assertEquals("moonlight://one,moonlight://two", inner.launchedUris());
}
```

- [x] **Step 2: Run tests to verify they fail**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.StreamConnectionLaunchUriTest
```

Expected: FAIL because `MainThreadDispatcher` and `DispatchingStreamConnectionLauncher` do not exist.

- [x] **Step 3: Implement the minimal wrapper**

Add `MainThreadDispatcher` with `void dispatch(Runnable action)`. Add `DispatchingStreamConnectionLauncher` that posts `inner.launch(launchUri)` through the dispatcher.

- [x] **Step 4: Run tests to verify they pass**

Run the same focused JVM test command. Expected: PASS.

### Task 2: Activity Wiring

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/AndroidMainThreadDispatcher.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`

- [x] **Step 1: Wire Android dispatcher**

Add `AndroidMainThreadDispatcher` backed by `Activity.runOnUiThread`, then wrap `AndroidIntentStreamConnectionLauncher` in `DispatchingStreamConnectionLauncher` inside `BeaconActivity.createModel()`.

- [x] **Step 2: Run Android build**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: PASS.

### Task 3: Docs, Validation, And Sync

**Files:**
- Modify: `README.md`

- [x] **Step 1: Document Android handoff hardening**

Update the Android section to say stream URI handoff is posted onto the Activity UI thread before delegating to Android `ACTION_VIEW`.

- [x] **Step 2: Run full validation**

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

Expected: all build/test/lint commands pass. The timeout-pattern audit should return no matches.

- [ ] **Step 3: Commit and sync**

Commit, push, open a pull request, wait for CI, and merge only after CI is green.
