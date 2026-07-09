# Milestone 82: Android Native Stream Router Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split Android native stream handling into a protocol router and protocol-specific clients so real decoder work can plug into the APK without expanding the current diagnostic client.

**Architecture:** Keep externally visible behavior identical. `DiagnosticNativeStreamClient` becomes a thin default composition over `NativeStreamClientRouter`, `BeaconTestNativeStreamClient`, and `GameStreamNativeStreamClient`. The router owns protocol selection and active-client stop delegation; protocol clients own only their protocol-specific start behavior.

**Tech Stack:** Java Android app, existing JVM unit tests, no Android device or emulator required for this refactor.

---

## Requirements

- `REQ-CTRL-009`: the APK consumes server policy and stream connection descriptors without reinterpreting display topology.
- `REQ-CTRL-013`: endpoint-only descriptors produce explicit client behavior or diagnostics.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-TEST-010`: real phone testing remains final confirmation for actual decoder compatibility.
- `REQ-SYNC-006`: docs, implementation, and validation status stay aligned.

## Non-Goals

- No real GameStream/Moonlight decoder implementation.
- No MediaCodec frame decode path.
- No network transport implementation.
- No server behavior changes.
- No new timeout, polling, or cancellation behavior.

## Files

- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/NativeStreamProtocolClient.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/NativeStreamClientRouter.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconTestNativeStreamClient.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/GameStreamNativeStreamClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/DiagnosticNativeStreamClient.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/NativeStreamClientRouterTest.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconTestNativeStreamClientTest.java`
- Test: `src/Beacon.Android/app/src/test/java/dev/beacon/android/GameStreamNativeStreamClientTest.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/DiagnosticNativeStreamClientTest.java`
- Modify: `README.md`

## Tasks

### Task 1: RED protocol client and router tests

- [x] **Step 1: Write failing JVM tests**

Add:

- `NativeStreamClientRouterTest` proving the router delegates to the first supporting protocol client, returns the connection's missing-launch-URI diagnostic when no protocol client supports the descriptor, ignores absent connections, and stops only the active successful protocol client.
- `BeaconTestNativeStreamClientTest` proving `beacon-test` still starts only with `video=beacon-test://pattern/color-bars` and preserves the existing status/presentation.
- `GameStreamNativeStreamClientTest` proving incomplete and complete endpoint-only GameStream descriptors still return the exact current diagnostics.
- A small assertion in `DiagnosticNativeStreamClientTest` proving the default client remains a facade and still handles the existing `beacon-test` happy path.

- [x] **Step 2: Run RED tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.NativeStreamClientRouterTest --tests dev.beacon.android.BeaconTestNativeStreamClientTest --tests dev.beacon.android.GameStreamNativeStreamClientTest --tests dev.beacon.android.DiagnosticNativeStreamClientTest
```

Expected: compile failure because the router/protocol client classes do not exist yet.

Observed: focused Gradle tests failed at compile time because `NativeStreamProtocolClient`, `NativeStreamClientRouter`, `BeaconTestNativeStreamClient`, and `GameStreamNativeStreamClient` were missing.

### Task 2: GREEN native stream router

- [x] **Step 1: Implement protocol client interface**

Add `NativeStreamProtocolClient`:

```java
package dev.beacon.android;

interface NativeStreamProtocolClient extends NativeStreamClient {
    boolean supports(StreamConnectionDescriptor connection);
}
```

- [x] **Step 2: Implement `NativeStreamClientRouter`**

Add a router that implements `NativeStreamClient`, accepts `NativeStreamProtocolClient... clients`, returns `NativeStreamStartResult.unsupported("")` for absent connections, starts the first supporting client, tracks it only when `start.success()` is true, and delegates `stop()` only to that active client.

- [x] **Step 3: Extract `BeaconTestNativeStreamClient`**

Move the current `beacon-test` color-bars behavior into `BeaconTestNativeStreamClient`. It supports only `protocol=beacon-test`, keeps the exact existing success and unsupported messages, and has a no-op `stop()`.

- [x] **Step 4: Extract `GameStreamNativeStreamClient`**

Move the current GameStream/Moonlight endpoint-map diagnostic behavior into `GameStreamNativeStreamClient`. It supports descriptors accepted by `GameStreamEndpointPlan`, only handles endpoint-only descriptors with no launch URI, keeps the exact incomplete-map and decoder-not-implemented messages, and returns the generic missing-launch-URI diagnostic for supported protocols that include a launch URI.

- [x] **Step 5: Make `DiagnosticNativeStreamClient` the default facade**

Replace its inlined protocol logic with:

```java
public final class DiagnosticNativeStreamClient implements NativeStreamClient {
    private final NativeStreamClientRouter router;

    public DiagnosticNativeStreamClient() {
        this(new NativeStreamClientRouter(
            new BeaconTestNativeStreamClient(),
            new GameStreamNativeStreamClient()));
    }

    DiagnosticNativeStreamClient(NativeStreamClientRouter router) {
        this.router = router;
    }

    @Override
    public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
        return router.start(connection);
    }

    @Override
    public void stop() {
        router.stop();
    }
}
```

- [x] **Step 6: Run GREEN tests**

Run the Task 1 Gradle command again. Expected: PASS.

Observed: focused native stream router/client tests passed.

### Task 3: Documentation and validation

- [x] **Step 1: Update README**

Document that Android native stream handling is now routed through protocol-specific clients, with `beacon-test` implemented and GameStream/Moonlight still diagnostic-only until real decoder work lands.

- [x] **Step 2: Validate**

Run:

```powershell
git diff --check
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
dotnet test Beacon.slnx --no-build
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: all commands pass; `rg` should return no matches.

Observed: `git diff --check`, the timeout/cancellation search, `dotnet test Beacon.slnx --no-build`, and Android `test assembleDebug` all passed.

- [ ] **Step 3: Commit and PR**

Commit with:

```powershell
git add README.md docs\superpowers\plans\2026-07-09-beacon-stream-milestone-82-android-native-stream-router.md src\Beacon.Android\app\src\main\java\dev\beacon\android\NativeStreamProtocolClient.java src\Beacon.Android\app\src\main\java\dev\beacon\android\NativeStreamClientRouter.java src\Beacon.Android\app\src\main\java\dev\beacon\android\BeaconTestNativeStreamClient.java src\Beacon.Android\app\src\main\java\dev\beacon\android\GameStreamNativeStreamClient.java src\Beacon.Android\app\src\main\java\dev\beacon\android\DiagnosticNativeStreamClient.java src\Beacon.Android\app\src\test\java\dev\beacon\android\NativeStreamClientRouterTest.java src\Beacon.Android\app\src\test\java\dev\beacon\android\BeaconTestNativeStreamClientTest.java src\Beacon.Android\app\src\test\java\dev\beacon\android\GameStreamNativeStreamClientTest.java src\Beacon.Android\app\src\test\java\dev\beacon\android\DiagnosticNativeStreamClientTest.java
git commit -m "Split Android native stream protocol routing"
```

Push, open a PR, verify CI, and merge when green.
