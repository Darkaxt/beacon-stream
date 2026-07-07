# Milestone 7: Session Ownership And Launch Boundary

## Objective

Make session cleanup server-owned instead of trusting APK/client booleans. Beacon must record the app launch attempt for a session, classify owned work through an interface, and use that server-owned snapshot when deciding whether a client virtual display can be removed.

This milestone adds the contracts, deterministic fakes, endpoint wiring, and diagnostics. Full Windows child-process enumeration and monitor-window enumeration are later Windows-backend work.

## Requirements Covered

- `REQ-SESS-001`: a session owns the process launched by the orchestrator.
- `REQ-SESS-002`: a session can represent child processes of the launched process through the ownership inspector contract.
- `REQ-SESS-003`: a session can represent new top-level windows on the client virtual display through the ownership inspector contract.
- `REQ-SESS-004`: unrelated processes or update windows do not block cleanup.
- `REQ-SESS-005`: quit-session behavior is based on owned session work.
- `REQ-SESS-007`: launch-app, close-app, disconnect can restore/remove only when owned work is gone.
- `REQ-DISP-007`: display removal still requires `client inactive AND no owned work remains`.
- `REQ-DISP-008`: no timeout replaces the cleanup rule.
- `REQ-TEST-007`: process/window tracking is interface-backed and fake-testable.
- `REQ-TEST-008`: fast tests validate cleanup rules.

## Task 1: Plan And Sync

Files:

- Add: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-7-session-ownership.md`

- [x] **Step 1: Create milestone branch**

```powershell
git switch -c codex/milestone-7-session-ownership
```

- [x] **Step 2: Commit plan**

```powershell
git add docs/superpowers/plans/2026-07-08-beacon-stream-milestone-7-session-ownership.md
git commit -m "Plan session ownership milestone"
git push -u origin codex/milestone-7-session-ownership
```

## Task 2: Add Game Launch Boundary

Files:

- Add: `src/Beacon.Core/Games/IGameLauncher.cs`
- Add: `src/Beacon.Core/Games/FakeGameLauncher.cs`
- Add: `src/Beacon.Platform.Windows/Games/WindowsGameLauncher.cs`
- Add: `tests/Beacon.Core.Tests/Games/FakeGameLauncherTests.cs`
- Add: `tests/Beacon.Platform.Windows.Tests/Games/WindowsGameLauncherTests.cs`

- [x] **Step 1: Write launch boundary tests**

Cover:

- fake launcher records launch intent and display id.
- failed launch returns a diagnostic error.
- Windows command builder handles `process`, `steam-app`, and `steam-rungameid` launch intents without starting anything in tests.

- [x] **Step 2: Add launch contracts**

Create:

- `GameLaunchRequest`
- `GameLaunchState`
- `GameLaunchResult`
- `IGameLauncher`

The request must include the resolved `GameDescriptor`, `SessionPlan`, and display id.

- [x] **Step 3: Add fake and Windows boundary implementation**

The Windows implementation may expose command creation separately from actual `Process.Start` so tests can validate behavior without launching games.

- [x] **Step 4: Verify tests and commit**

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter GameLauncher
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter WindowsGameLauncher
git add src/Beacon.Core src/Beacon.Platform.Windows tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-7-session-ownership.md
git commit -m "Add game launch boundary"
git push
```

## Task 3: Add Session Ownership Tracker

Files:

- Add: `src/Beacon.Core/Sessions/ISessionOwnershipTracker.cs`
- Add: `src/Beacon.Core/Sessions/SessionOwnershipSnapshot.cs`
- Add: `src/Beacon.Core/Sessions/SessionOwnershipTracker.cs`
- Add: `src/Beacon.Core/Sessions/FakeSessionActivityInspector.cs`
- Add: `tests/Beacon.Core.Tests/Sessions/SessionOwnershipTrackerTests.cs`

- [x] **Step 1: Write ownership tests**

Cover:

- launched process running blocks cleanup.
- child process running blocks cleanup.
- owned top-level window remaining blocks cleanup.
- unrelated activity reported outside the session does not block cleanup.
- empty owned snapshot allows cleanup.

- [x] **Step 2: Add ownership contracts**

Create:

- `SessionOwnershipRecord`
- `SessionOwnershipSnapshot`
- `ISessionActivityInspector`
- `ISessionOwnershipTracker`

- [x] **Step 3: Implement in-memory tracker**

The tracker records launch state by `sessionId`, asks the inspector for current owned activity, and returns a snapshot with explicit reasons.

- [x] **Step 4: Verify tests and commit**

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter SessionOwnershipTracker
git add src/Beacon.Core tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-7-session-ownership.md
git commit -m "Add session ownership tracker"
git push
```

## Task 4: Wire Server Launch And Quit

Files:

- Modify: `src/Beacon.Server/Program.cs`
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`
- Modify: `src/Beacon.FakeEndpoint/FakeEndpointRunner.cs`
- Modify: `src/Beacon.ClientLab/src/main.ts`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconApiClient.java`

- [x] **Step 1: Add endpoint tests**

Cover:

- launch records ownership after display lease and before stream success response.
- launch failure attempts physical-primary restore and returns 503.
- quit ignores stale client owned-work flags and uses server-owned snapshot.
- quit removes display after server-owned work disappears.

- [x] **Step 2: Register services**

Register `IGameLauncher` and `ISessionOwnershipTracker` using fake implementations by default until real launch is explicitly selected.

- [x] **Step 3: Wire launch endpoint**

After display lease success and before stream start, call the launcher and record the resulting launch state. If launch fails, restore physical primary and return an explicit 503.

- [x] **Step 4: Wire quit endpoint**

Use `ISessionOwnershipTracker.GetSnapshotAsync` for owned process/window state. The request may still carry `clientActive`; it must not decide owned work.

- [x] **Step 5: Update clients**

Remove owned-process/window booleans from Client Lab and Android quit payloads where possible. Keep server DTO compatibility for older clients but ignore those fields for ownership decisions.

- [x] **Step 6: Verify tests and commit**

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter ClientApiTests
pnpm --dir src\Beacon.ClientLab test
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test
git add src tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-7-session-ownership.md
git commit -m "Use server-owned session cleanup state"
git push
```

## Task 5: Diagnostics, Validation, And PR

Files:

- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitModels.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs`
- Modify: `src/Beacon.Cockpit/MainWindow.xaml`
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-7-session-ownership.md`

- [x] **Step 1: Expose ownership diagnostics**

Admin snapshot and cockpit should show session ownership summaries: session id, app id, launched process id if known, process running, child process running, owned window remaining, and reasons.

- [x] **Step 2: Full validation**

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

- [x] **Step 3: Boundary audit**

Run:

```powershell
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
rg "ownedProcessRunning|ownedWindowRemaining" src/Beacon.Server src/Beacon.ClientLab src/Beacon.Android -n
```

Expected:

- No timeout-driven cancellation behavior.
- Old owned-work fields may exist only for compatibility DTOs/tests, not as the server cleanup authority.

- [x] **Step 4: Commit docs and validation**

```powershell
git add README.md src tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-7-session-ownership.md
git commit -m "Document session ownership milestone"
git push
```

- [ ] **Step 5: Open PR, wait for CI, mark ready, merge**

```powershell
gh pr create --draft --base main --head codex/milestone-7-session-ownership --title "Add server-owned session cleanup state" --body "Milestone 7 session ownership implementation."
gh pr checks --watch
gh pr ready
gh pr merge --merge --delete-branch
```

## Out Of Scope

- Full Windows child-process tree implementation.
- Full Windows top-level window to monitor/display classification.
- Moving or closing windows.
- Launching games inside a real virtual desktop from tests.
- Timeout-driven cleanup.
