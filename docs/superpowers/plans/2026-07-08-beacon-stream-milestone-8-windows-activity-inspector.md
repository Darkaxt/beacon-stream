# Milestone 8: Windows Session Activity Inspector

## Objective

Replace the purely fake session activity boundary with a real Windows inspector that can determine whether a launched session still owns running work. The inspector must detect launched-process liveness, child-process liveness, and top-level windows on the session's virtual display through a fakeable Windows API.

This milestone does not move, close, or terminate windows. It only reports owned activity so the server cleanup rule remains evidence-based.

## Requirements Covered

- `REQ-SESS-001`: detect whether the launched process is still running.
- `REQ-SESS-002`: detect whether child processes of the launched process are still running.
- `REQ-SESS-003`: detect top-level windows on the client's virtual display that belong to the launched process, child process, or new process after session start.
- `REQ-SESS-004`: unrelated processes or unrelated update windows do not block cleanup.
- `REQ-SESS-005`: quit-session behavior can rely on real server-side owned work.
- `REQ-DISP-007`: virtual display removal remains gated by owned work.
- `REQ-TEST-007`: process/window tracking stays interface-backed and fake-testable.
- `REQ-TEST-009`: real Windows integration remains separated from fast tests.

## Task 1: Plan And Sync

Files:

- Add: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-8-windows-activity-inspector.md`

- [x] **Step 1: Create milestone branch**

```powershell
git switch -c codex/milestone-8-windows-activity-inspector
```

- [x] **Step 2: Commit plan**

```powershell
git add docs/superpowers/plans/2026-07-08-beacon-stream-milestone-8-windows-activity-inspector.md
git commit -m "Plan Windows activity inspector milestone"
git push -u origin codex/milestone-8-windows-activity-inspector
```

## Task 2: Add Windows Activity API Boundary

Files:

- Add: `src/Beacon.Platform.Windows/Sessions/IWindowsSessionActivityApi.cs`
- Add: `src/Beacon.Platform.Windows/Sessions/WindowsSessionActivityApi.cs`
- Add: `tests/Beacon.Platform.Windows.Tests/Sessions/FakeWindowsSessionActivityApi.cs`
- Add: `tests/Beacon.Platform.Windows.Tests/Sessions/WindowsSessionActivityInspectorTests.cs`

- [x] **Step 1: Write fast fake-boundary tests**

Cover:

- launched process still running.
- child process still running.
- child process window on virtual display.
- unrelated window on virtual display does not block cleanup when it is not a child and started before session start.
- new process window on virtual display after session start blocks cleanup.

- [x] **Step 2: Add API contract**

The contract must expose:

- process liveness by process id.
- child process ids by parent id.
- process start time where available.
- visible top-level windows with process id, title, and rectangle.

- [x] **Step 3: Implement Windows API**

Use platform APIs directly:

- `Process.GetProcessById` / `Process.GetProcesses`.
- parent-process lookup through `NtQueryInformationProcess`.
- top-level window enumeration through `EnumWindows`, `IsWindowVisible`, `GetWindowThreadProcessId`, `GetWindowRect`, and window text APIs.

No timeout-driven cancellation behavior.

## Task 3: Implement Inspector

Files:

- Add: `src/Beacon.Platform.Windows/Sessions/WindowsSessionActivityInspector.cs`
- Modify: `src/Beacon.Platform.Windows/Beacon.Platform.Windows.csproj` if needed.

- [x] **Step 1: Implement process activity**

`LaunchedProcessRunning` is true when the launch state has a process id and the API reports it running.

- [x] **Step 2: Implement child-process activity**

`ChildProcessRunning` is true when any child of the launched process is running.

- [x] **Step 3: Implement top-level window activity**

`OwnedWindowRemaining` is true when a visible top-level window intersects the planned display rectangle and either:

- belongs to the launched process,
- belongs to a child process,
- or belongs to a process that started after session start.

Unrelated old windows on the virtual display must not block cleanup.

- [x] **Step 4: Verify focused tests and commit**

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter WindowsSessionActivityInspector
git add src/Beacon.Platform.Windows tests/Beacon.Platform.Windows.Tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-8-windows-activity-inspector.md
git commit -m "Add Windows session activity inspector"
git push
```

## Task 4: Registration And Diagnostics

Files:

- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-8-windows-activity-inspector.md`

- [x] **Step 1: Document platform registration boundary**

The default server still uses fake services until a Windows-host mode is selected. Document how the Windows inspector is intended to replace `FakeSessionActivityInspector` in the Windows service composition.

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
```

Expected: no timeout-driven cancellation behavior.

- [x] **Step 4: Commit docs and validation**

```powershell
git add README.md docs/extraction-map.md docs/superpowers/plans/2026-07-08-beacon-stream-milestone-8-windows-activity-inspector.md
git commit -m "Document Windows activity inspector milestone"
git push
```

- [ ] **Step 5: Open PR, wait for CI, mark ready, merge**

```powershell
gh pr create --draft --base main --head codex/milestone-8-windows-activity-inspector --title "Add Windows session activity inspector" --body "Milestone 8 Windows activity inspector implementation."
gh pr checks --watch
gh pr ready
gh pr merge --merge --delete-branch
```

## Out Of Scope

- Moving, closing, or terminating windows.
- Real server composition switch to Windows host mode.
- Phone testing.
- Streaming protocol/input integration.
- Timeout-driven cleanup.
