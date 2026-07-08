# Beacon Stream Milestone 13: Cockpit Profile Editing

## Objective

Let the local WPF cockpit inspect and edit server-side client profiles now that profiles can be persisted. The APK remains constrained to its allowlist, while the cockpit uses a local-admin endpoint that may edit display behavior policy and recovery-related profile settings.

## Requirements Covered

- `REQ-CTRL-010`: The WPF cockpit may edit server-global settings, client profiles, recovery actions, and diagnostics because it is a local server-admin surface.
- `REQ-CTRL-011`: APK profile patches remain constrained to the existing allowlist.
- `REQ-CTRL-012`: Display mode, blackout, mirror prohibition, persistence, destruction, restore, and recovery safety policies are WPF/server-admin controlled in version 1.
- `REQ-PROFILE-001`: Each registered client has a server-side client profile.
- `REQ-PROFILE-002`: Profiles include virtual desktop geometry, refresh rate, HDR preference, stream preferences, audio mode, display behavior policy, and disconnect/recovery preferences.
- `REQ-PROFILE-004`: The Z Fold 7 profile must not be collapsed from `2560x1600` to `2560x1440`.
- `REQ-PROFILE-005`: The profile supports virtual-primary behavior while restoring or extending physical displays.
- `REQ-REC-001`: Recovery is first-class and visible in the local admin surface.

## Implementation Plan

1. Add failing server tests for a local-admin profile patch endpoint:
   - admin can update profile geometry and display/session policy fields;
   - admin patch rejects the Z Fold 7 `2560x1440` collapse;
   - APK/client patch endpoint still rejects policy fields.
2. Add failing cockpit tests:
   - snapshot parsing carries profile details, not just client ids;
   - selecting a client populates editable profile fields;
   - save delegates to an admin profile patch endpoint.
3. Implement the server-side admin patch model and validation.
4. Extend cockpit API/viewmodel and XAML with a compact client profile editor.
5. Update README with the admin/client patch boundary.

## Non-Goals

- Global settings editing.
- Pairing-token management UI.
- Native streaming/capture/input work.
- Real-time validation against live display driver modes.
- Any APK expansion beyond the already allowed client fields.

## Validation

- `dotnet format Beacon.slnx --verify-no-changes`
- `dotnet build Beacon.slnx -warnaserror`
- `dotnet test Beacon.slnx`
- `pnpm --dir src\Beacon.ClientLab lint`
- `pnpm --dir src\Beacon.ClientLab test`
- `pnpm --dir tests\Beacon.ClientLab.Playwright lint`
- `pnpm --dir tests\Beacon.ClientLab.Playwright test`
- `gradle -p src\Beacon.Android test assembleDebug`
- `rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n`

## Sync Plan

Commit and push this plan before implementation. After tests and implementation pass locally, push the implementation, open a draft PR, wait for GitHub checks, then mark ready and merge.
