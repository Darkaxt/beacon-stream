# Milestone 11: Manual Recovery Actions

## Objective

Add explicit manual recovery actions for the local admin path. The server and WPF cockpit should expose the practical rescue operations from the ApolloDisplayRescue workflow: move virtual-display windows back to the physical desktop, close virtual-display windows, terminate processes with windows on virtual displays, and keep physical-primary restore available.

These actions are manual escape hatches. They must not replace the orchestrator lifecycle rules, and they must not run automatically.

## Requirements Covered

- `REQ-REC-001`: recovery is a first-class feature.
- `REQ-REC-002`: cockpit exposes restore physical primary, move windows back, close windows on virtual display, terminate processes on virtual display, and reset-style recovery actions.
- `REQ-REC-004`: moving windows back supports minimizing moved windows.
- `REQ-REC-006`: WPF may perform broader local admin recovery than the APK.
- `REQ-REC-007`: recovery remains an explicit escape hatch, not lifecycle policy.
- `REQ-REC-008`: recovery diagnostics report selected actions and counts.
- `REQ-DISP-014`: physical-primary restore remains an explicit recovery action.
- `REQ-DISP-016`: the server has a manual path to prevent the laptop panel from staying stranded.
- `REQ-TEST-001`: validation remains phone-free.
- `REQ-TEST-007`: recovery uses fakeable boundaries.

## Task 1: Plan And Sync

Files:

- Add: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-11-recovery-actions.md`

- [x] **Step 1: Create milestone branch**

```powershell
git switch -c codex/milestone-11-recovery-actions
```

- [x] **Step 2: Commit plan**

```powershell
git add docs/superpowers/plans/2026-07-08-beacon-stream-milestone-11-recovery-actions.md
git commit -m "Plan manual recovery actions milestone"
git push -u origin codex/milestone-11-recovery-actions
```

## Task 2: Add Recovery Backend Contract And API Endpoints

Files:

- Add: `src/Beacon.Core/Recovery/IRecoveryBackend.cs`
- Add: `src/Beacon.Core/Recovery/FakeRecoveryBackend.cs`
- Add: `tests/Beacon.Core.Tests/Recovery/FakeRecoveryBackendTests.cs`
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs`

- [x] **Step 1: Add contract and fake backend**

Support:

- move virtual-display windows to physical display and minimize them.
- close virtual-display windows.
- terminate processes with virtual-display windows.

- [x] **Step 2: Register fake recovery by default**

Default fake host mode must use `FakeRecoveryBackend`.

- [x] **Step 3: Add admin endpoints**

Add:

- `POST /admin/recovery/move-windows-back`
- `POST /admin/recovery/close-virtual-windows`
- `POST /admin/recovery/terminate-virtual-processes`

- [x] **Step 4: Verify focused tests and commit**

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter FakeRecoveryBackend
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter AdminApiTests
git add src/Beacon.Core src/Beacon.Server tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-11-recovery-actions.md
git commit -m "Add manual recovery endpoints"
git push
```

## Task 3: Implement Windows Recovery Boundary

Files:

- Add: `src/Beacon.Platform.Windows/Recovery/IWindowsRecoveryApi.cs`
- Add: `src/Beacon.Platform.Windows/Recovery/WindowsRecoveryApi.cs`
- Add: `src/Beacon.Platform.Windows/Recovery/WindowsRecoveryBackend.cs`
- Add: `tests/Beacon.Platform.Windows.Tests/Recovery/WindowsRecoveryBackendTests.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`

- [ ] **Step 1: Add fakeable Windows API boundary**

The backend must not P/Invoke directly in tests.

- [ ] **Step 2: Move windows from virtual to physical display**

Use the current active topology and visible top-level windows. Move matching windows to the physical primary or first physical display and minimize them when requested.

- [ ] **Step 3: Close virtual-display windows**

Send close requests only for visible windows intersecting virtual displays.

- [ ] **Step 4: Terminate virtual-display processes**

Terminate distinct process ids owning visible windows on virtual displays. Do not terminate the current Beacon process.

- [ ] **Step 5: Register Windows recovery in Windows host mode**

Fake host mode remains fake. Windows host mode registers `WindowsRecoveryBackend`.

- [ ] **Step 6: Verify focused tests and commit**

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter WindowsRecoveryBackend
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter BeaconServiceRegistration
git add src/Beacon.Platform.Windows src/Beacon.Server tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-11-recovery-actions.md
git commit -m "Implement Windows manual recovery backend"
git push
```

## Task 4: Add Cockpit Recovery Controls

Files:

- Modify: `src/Beacon.Cockpit/Cockpit/CockpitApiClient.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs`
- Modify: `src/Beacon.Cockpit/MainWindow.xaml`
- Modify: `tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs`
- Modify: `tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs`

- [ ] **Step 1: Add cockpit API methods**

Expose the three new admin endpoints.

- [ ] **Step 2: Add view-model commands**

Add commands for move all back, close virtual windows, and terminate virtual processes.

- [ ] **Step 3: Add Recovery tab buttons**

Keep the UI simple and functional. The move-all-back action should request minimized moved windows.

- [ ] **Step 4: Verify focused tests and commit**

```powershell
dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj
git add src/Beacon.Cockpit tests/Beacon.Cockpit.Tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-11-recovery-actions.md
git commit -m "Add cockpit manual recovery actions"
git push
```

## Task 5: Docs, Validation, And PR

Files:

- Modify: `README.md`
- Modify: `docs/windows-display-backend.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-11-recovery-actions.md`

- [ ] **Step 1: Document recovery actions**

Document the admin endpoints and the cockpit actions.

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
```

Expected: no timeout-driven cancellation behavior.

- [ ] **Step 4: Commit docs and validation**

```powershell
git add README.md docs/windows-display-backend.md docs/superpowers/plans/2026-07-08-beacon-stream-milestone-11-recovery-actions.md
git commit -m "Document manual recovery actions milestone"
git push
```

- [ ] **Step 5: Open PR, wait for CI, mark ready, merge**

```powershell
gh pr create --draft --base main --head codex/milestone-11-recovery-actions --title "Add manual recovery actions" --body "Milestone 11 manual recovery actions implementation."
gh pr checks --watch
gh pr ready
gh pr merge --merge --delete-branch
```

## Out Of Scope

- Automatic recovery policy changes.
- UAC elevation implementation.
- Phone recovery UI.
- Killing unrelated processes automatically.
- Native streaming protocol work.
