# Milestone 9: Windows Host Composition

## Objective

Add an explicit server host mode switch so Beacon can run in deterministic fake mode by default and in Windows host mode only when requested. Windows host mode wires real Windows display, game launch, and session activity inspection boundaries. It must not become the default yet.

This milestone is about composition and diagnostics, not about running real display or game-launch side effects during tests.

## Requirements Covered

- `REQ-CTRL-001`: server remains source of truth for desired state.
- `REQ-DISP-003`: real Windows display lease backend can be selected for preflight.
- `REQ-DISP-011`: real mode still refuses physical-display fallback when virtual display is unavailable.
- `REQ-SESS-001` through `REQ-SESS-003`: Windows mode can use real launcher and activity inspector boundaries.
- `REQ-REC-008`: diagnostics must expose selected backend mode.
- `REQ-TEST-001`: default mode remains phone-free and side-effect-safe.
- `REQ-TEST-009`: real Windows integration stays explicitly selected.

## Task 1: Plan And Sync

Files:

- Add: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-9-windows-host-composition.md`

- [x] **Step 1: Create milestone branch**

```powershell
git switch -c codex/milestone-9-windows-host-composition
```

- [ ] **Step 2: Commit plan**

```powershell
git add docs/superpowers/plans/2026-07-08-beacon-stream-milestone-9-windows-host-composition.md
git commit -m "Plan Windows host composition milestone"
git push -u origin codex/milestone-9-windows-host-composition
```

## Task 2: Add Host Mode Options

Files:

- Add: `src/Beacon.Server/Hosting/BeaconHostMode.cs`
- Add: `src/Beacon.Server/Hosting/BeaconHostOptions.cs`
- Add: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `src/Beacon.Server/Program.cs`
- Add: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`

- [ ] **Step 1: Write registration tests**

Cover:

- default host mode is fake.
- `windows` mode registers Windows display, launcher, and activity inspector services.
- unknown mode fails with a clear configuration error.

- [ ] **Step 2: Add options and parser**

Read host mode from configuration key `Beacon:HostMode` or environment variable `BEACON_HOST_MODE`.

Allowed values:

- `fake`
- `windows`

- [ ] **Step 3: Move service registration into helper**

Keep `Program.cs` small and make registration testable.

- [ ] **Step 4: Verify focused tests and commit**

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter BeaconServiceRegistration
git add src/Beacon.Server tests/Beacon.Server.Tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-9-windows-host-composition.md
git commit -m "Add server host mode registration"
git push
```

## Task 3: Wire Explicit Windows Mode

Files:

- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `src/Beacon.Server/appsettings.json`
- Modify: `src/Beacon.Server/appsettings.Development.json`
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`

- [ ] **Step 1: Fake mode remains default**

Default registration must keep `FakeDisplayBackend`, `FakeGameLauncher`, `FakeSessionActivityInspector`, and `FakeStreamingBackend`.

- [ ] **Step 2: Windows mode registers real boundaries**

Register:

- `IWindowsDisplayApi` -> `WindowsDisplayApi`
- `IDisplayBackend` -> `WindowsDisplayBackend`
- `IGameLauncher` -> `WindowsGameLauncher`
- `IWindowsSessionActivityApi` -> `WindowsSessionActivityApi`
- `ISessionActivityInspector` -> `WindowsSessionActivityInspector`

Keep `IStreamingBackend` fake until a real streaming wrapper is selected explicitly.

- [ ] **Step 3: Expose selected mode in admin snapshot**

Add `host.mode` and backend names to `/admin/snapshot`.

- [ ] **Step 4: Verify tests and commit**

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter "BeaconServiceRegistration|AdminApiTests"
git add src/Beacon.Server tests/Beacon.Server.Tests docs/superpowers/plans/2026-07-08-beacon-stream-milestone-9-windows-host-composition.md
git commit -m "Wire explicit Windows host mode"
git push
```

## Task 4: Docs, Validation, And PR

Files:

- Modify: `README.md`
- Modify: `docs/windows-display-backend.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-9-windows-host-composition.md`

- [ ] **Step 1: Document run commands**

Document:

```powershell
dotnet run --project src\Beacon.Server
$env:BEACON_HOST_MODE='windows'; dotnet run --project src\Beacon.Server
```

Make clear that Windows mode can create displays and launch apps, so it is explicit only.

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
git add README.md docs/windows-display-backend.md docs/superpowers/plans/2026-07-08-beacon-stream-milestone-9-windows-host-composition.md
git commit -m "Document Windows host composition milestone"
git push
```

- [ ] **Step 5: Open PR, wait for CI, mark ready, merge**

```powershell
gh pr create --draft --base main --head codex/milestone-9-windows-host-composition --title "Add explicit Windows host mode" --body "Milestone 9 Windows host composition implementation."
gh pr checks --watch
gh pr ready
gh pr merge --merge --delete-branch
```

## Out Of Scope

- Making Windows mode the default.
- Starting the server in Windows mode during tests.
- Real streaming wrapper selection.
- Driver installation.
- Phone testing.
