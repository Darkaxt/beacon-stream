# Milestone 84: Beacon Test Encoded Video Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the server-side `beacon-test` streaming backend explicitly emit the Android-readable encoded-video descriptor added in milestone 83.

**Architecture:** Keep `beacon-test` color bars as the default diagnostic stream. Add a small backend option for `color-bars` versus `encoded-video`, wire it through server configuration, and make the encoded-video connection descriptor carry `streamKind=encoded-video`, codec/container/dimensions/fps metadata, and a video endpoint the APK can validate in an emulator.

**Tech Stack:** .NET 10/C#, Beacon.Core streaming backend tests, Beacon.Server DI/API tests, README docs.

---

### Task 1: Core Backend Contract

**Files:**
- Modify: `tests/Beacon.Core.Tests/Streaming/BeaconTestStreamingBackendTests.cs`
- Modify: `src/Beacon.Core/Streaming/BeaconTestStreamingBackend.cs`

- [x] **Step 1: Write the failing core tests**

Add tests that instantiate `BeaconTestStreamingBackend` with encoded-video options and assert:
- `StartAsync` returns `protocol=beacon-test`.
- `launchUri` remains null.
- the video endpoint is `beacon-test://video/color-bars.h264`.
- metadata includes `streamKind=encoded-video`, `codec=h264`, `container=annex-b`, `width=2560`, `height=1600`, `fps=120`, `displayId`, and `transport`.
- health advertises the encoded-video endpoint and H.264 capability when encoded-video mode is configured.

- [x] **Step 2: Run RED core tests**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter BeaconTestStreamingBackendTests
```

Expected: compile failure because `BeaconTestStreamingOptions` and `BeaconTestStreamKind` do not exist yet.

Observed: compile failed because `BeaconTestStreamingOptions`, `BeaconTestStreamKind`, and the options constructor did not exist.

- [x] **Step 3: Implement minimal core backend options**

Add:
- `BeaconTestStreamKind` with `ColorBars` and `EncodedVideo`.
- `BeaconTestStreamingOptions` with a default of `ColorBars`.
- a constructor overload on `BeaconTestStreamingBackend`.
- encoded-video descriptor generation using plan width, height, and fps.

- [x] **Step 4: Run GREEN core tests**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter BeaconTestStreamingBackendTests
```

Expected: all `BeaconTestStreamingBackendTests` pass.

Observed: 5/5 `BeaconTestStreamingBackendTests` passed.

### Task 2: Server Configuration And API Surface

**Files:**
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `README.md`

- [x] **Step 1: Write the failing server tests**

Add tests that assert:
- `Beacon:Streaming:BeaconTest:StreamKind=encoded-video` registers `BeaconTestStreamingBackend` with encoded-video health.
- `BEACON_TEST_STREAM_KIND` overrides configuration.
- invalid stream kind fails with a clear error naming `color-bars` and `encoded-video`.
- a launch using encoded-video mode returns the encoded-video connection descriptor and metadata in the JSON response.

- [x] **Step 2: Run RED server tests**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "BeaconServiceRegistrationTests|LaunchCanReturnBeaconTestEncodedVideoStreamConnection"
```

Expected: compile failure because server registration does not expose BeaconTest stream-kind configuration.

Observed: compile failed because `BeaconTestStreamKindConfigurationKey`, `environmentBeaconTestStreamKind`, and the core option types did not exist.

- [x] **Step 3: Implement server configuration**

Add:
- `Beacon:Streaming:BeaconTest:StreamKind`
- `BEACON_TEST_STREAM_KIND`
- DI registration of `BeaconTestStreamingOptions`
- resolution values `color-bars` and `encoded-video`
- a clear invalid configuration error.

- [x] **Step 4: Update docs**

Document:
- default `beacon-test` still emits color bars.
- encoded-video mode is selected with `BEACON_TEST_STREAM_KIND=encoded-video`.
- encoded-video is still a contract/preflight path until real MediaCodec decode exists.

- [x] **Step 5: Run GREEN server tests**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "BeaconServiceRegistrationTests|LaunchCanReturnBeaconTestEncodedVideoStreamConnection"
```

Expected: selected server registration and API tests pass.

Observed: 22 selected server registration/API tests passed.

### Task 3: Validation And Sync

**Files:**
- Verify all files touched in Tasks 1 and 2.

- [x] **Step 1: Static checks**

Run:

```powershell
git diff --check
rg -n "Thread\.Sleep|Task\.Delay|CancelAfter|CancellationTokenSource\(|Timeout" src tests
```

Expected: no whitespace errors; no new timeout/cancellation pattern introduced by this milestone.

Observed: `git diff --check` passed, and the diff-only timeout-pattern scan returned no matches.

- [x] **Step 2: Dynamic checks**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter BeaconTestStreamingBackendTests
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "BeaconServiceRegistrationTests|LaunchCanReturnBeaconTestEncodedVideoStreamConnection"
```

Expected: both commands pass.

Observed: focused core/server tests passed, `dotnet test Beacon.slnx` passed, and Android `test assembleDebug` passed through the pinned Gradle binary.

- [ ] **Step 3: Sync**

Commit, push, open a PR, wait for CI, and merge if checks are green.

```powershell
git add README.md docs/superpowers/plans/2026-07-09-beacon-stream-milestone-84-beacon-test-encoded-video-mode.md src/Beacon.Core/Streaming/BeaconTestStreamingBackend.cs src/Beacon.Server/Hosting/BeaconServiceRegistration.cs tests/Beacon.Core.Tests/Streaming/BeaconTestStreamingBackendTests.cs tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs tests/Beacon.Server.Tests/ClientApiTests.cs
git commit -m "Add beacon-test encoded video mode"
git push -u origin codex/milestone-84-beacon-test-encoded-video-mode
gh pr create --title "Add beacon-test encoded video mode" --body "Adds explicit beacon-test encoded-video stream-kind configuration so the server can emit the Android-readable encoded-video descriptor from the real backend path."
```
