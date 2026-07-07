# Milestone 10: Streaming Backend Selection And Preflight

## Objective

Make streaming backend selection explicit and testable. Fake streaming remains the default. An external-process backend can be selected only through configuration and must preflight before display creation or game launch so missing wrapper configuration cannot churn topology or start an app first.

This milestone is still a boundary milestone. It does not copy Sunshine source, implement native video capture, require a phone, or require a real streaming executable during tests.

## Requirements Covered

- `REQ-CTRL-008`: streaming readiness is checked from the effective session plan before display topology or launch side effects.
- `REQ-CTRL-009`: the server, not the client, selects the streaming backend behavior.
- `REQ-DISP-011`: launch must not silently fall back to another stream/display path.
- `REQ-SESS-008`: stream backend failure must not leave the display or launched app unmanaged.
- `REQ-NET-003`: codec, FPS, bitrate, transport, and congestion policy are passed to the selected backend.
- `REQ-NET-005`: 120 FPS remains explicit in streaming state.
- `REQ-REC-008`: diagnostics expose selected streaming backend and readiness failures.
- `REQ-TEST-001`: default validation remains phone-free and side-effect-safe.
- `REQ-TEST-007`: streaming remains interface-backed and fakeable.
- `REQ-TEST-010`: real phone remains final confirmation, not required here.

## Task 1: Plan And Sync

Files:

- Add: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-10-streaming-selection.md`

- [x] **Step 1: Create milestone branch**

```powershell
git switch -c codex/milestone-10-streaming-selection
```

- [ ] **Step 2: Commit plan**

```powershell
git add docs/superpowers/plans/2026-07-08-beacon-stream-milestone-10-streaming-selection.md
git commit -m "Plan streaming backend selection milestone"
git push -u origin codex/milestone-10-streaming-selection
```

## Task 2: Add Streaming Backend Options And Preflight Contract

Files:

- Modify: `src/Beacon.Core/Streaming/IStreamingBackend.cs`
- Modify: `src/Beacon.Core/Streaming/FakeStreamingBackend.cs`
- Modify: `tests/Beacon.Core.Tests/Streaming/FakeStreamingBackendTests.cs`
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`

- [ ] **Step 1: Extend streaming contract**

Add a preflight method that validates whether the backend can start the given `SessionPlan` without starting the stream.

- [ ] **Step 2: Fake backend supports deterministic preflight**

Fake streaming must report readiness by default and allow tests to inject a preflight error.

- [ ] **Step 3: Launch checks preflight before display lease**

`POST /clients/{clientId}/launch` must run streaming preflight before `DisplayLeaseManager.EnsureLeaseAsync` and before `IGameLauncher.LaunchAsync`.

- [ ] **Step 4: Verify focused tests and commit**

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter FakeStreamingBackend
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
git add src/Beacon.Core src/Beacon.Server tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-10-streaming-selection.md
git commit -m "Preflight streaming backend before launch"
git push
```

## Task 3: Implement External Process Streaming Backend

Files:

- Modify: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`
- Modify: `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`

- [ ] **Step 1: Add backend options**

Represent the executable path as typed options. Missing path should produce a clear preflight failure.

- [ ] **Step 2: Add fakeable process runner boundary**

Start and stop external processes through an interface so tests never launch a real wrapper.

- [ ] **Step 3: Implement `IStreamingBackend`**

External backend must:

- build the existing command/environment from the session plan.
- preflight the configured executable path.
- record running/stopped session state.
- stop the owned wrapper process when asked.
- surface start/stop failures as diagnostics.

- [ ] **Step 4: Verify focused tests and commit**

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackend
git add src/Beacon.Platform.Windows tests/Beacon.Platform.Windows.Tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-10-streaming-selection.md
git commit -m "Implement external process streaming backend"
git push
```

## Task 4: Wire Explicit Streaming Selection

Files:

- Modify: `src/Beacon.Server/Hosting/BeaconHostOptions.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `src/Beacon.Server/appsettings.json`
- Modify: `src/Beacon.Server/appsettings.Development.json`
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`
- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs`

- [ ] **Step 1: Add streaming mode parser**

Read streaming backend mode from configuration key `Beacon:Streaming:Backend` or environment variable `BEACON_STREAMING_BACKEND`.

Allowed values:

- `fake`
- `external-process`

- [ ] **Step 2: Keep fake streaming default**

Default registration must still use `FakeStreamingBackend`.

- [ ] **Step 3: External mode registers explicit Windows backend**

External mode must register `ExternalProcessStreamingBackend` and its process runner. It must never be implied by Windows host mode alone.

- [ ] **Step 4: Expose mode in diagnostics**

`/admin/snapshot` must continue exposing the selected streaming backend name.

- [ ] **Step 5: Verify tests and commit**

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "BeaconServiceRegistration|AdminApiTests|ClientApiTests"
git add src/Beacon.Server tests/Beacon.Server.Tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-10-streaming-selection.md
git commit -m "Wire explicit streaming backend selection"
git push
```

## Task 5: Docs, Validation, And PR

Files:

- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-10-streaming-selection.md`

- [ ] **Step 1: Document run commands**

Document:

```powershell
dotnet run --project src\Beacon.Server
$env:BEACON_STREAMING_BACKEND='external-process'; $env:BEACON_EXTERNAL_STREAMING_EXECUTABLE='C:\Tools\beacon-stream-wrapper.exe'; dotnet run --project src\Beacon.Server
```

- [ ] **Step 2: Full validation**

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
```

- [ ] **Step 3: Boundary audit**

Run:

```powershell
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
rg "LizardByte|Sunshine/src|nvhttp|rtsp|moonlight" src tests -n
```

Expected:

- no timeout-driven cancellation behavior.
- Sunshine/Moonlight matches only in documentation or boundary names; no copied upstream source.

- [ ] **Step 4: Commit docs and validation**

```powershell
git add README.md docs/extraction-map.md docs/superpowers/plans/2026-07-08-beacon-stream-milestone-10-streaming-selection.md
git commit -m "Document streaming backend selection milestone"
git push
```

- [ ] **Step 5: Open PR, wait for CI, mark ready, merge**

```powershell
gh pr create --draft --base main --head codex/milestone-10-streaming-selection --title "Add explicit streaming backend selection" --body "Milestone 10 streaming backend selection and preflight implementation."
gh pr checks --watch
gh pr ready
gh pr merge --merge --delete-branch
```

## Out Of Scope

- Copying Sunshine source.
- Native capture, encode, audio, RTSP, or input forwarding.
- Phone testing.
- Making external streaming the default.
- Driver installation.
