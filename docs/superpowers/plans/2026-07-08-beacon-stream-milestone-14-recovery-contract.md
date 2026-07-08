# Beacon Stream Milestone 14: Recovery Contract Completion

## Objective

Complete the remaining local-admin recovery actions required by the design before moving deeper into native streaming. The WPF cockpit already supports physical restore, window move/close/terminate, and client display recovery. This milestone adds explicit selected-client stream stop and selected-client display lease removal actions through `/admin` endpoints.

## Requirements Covered

- `REQ-REC-001`: Recovery is a first-class feature.
- `REQ-REC-002`: The WPF cockpit must expose restore physical primary, move windows back, close windows on virtual display, terminate owned processes on virtual display, remove virtual display lease, stop stream, and reset topology actions.
- `REQ-REC-004`: Moving windows back keeps the existing minimized behavior.
- `REQ-REC-005`: The APK may call emergency actions for its own active session; this milestone keeps broader actions under `/admin`.
- `REQ-REC-006`: The WPF cockpit may perform broader local admin recovery than the APK.
- `REQ-REC-007`: Recovery tooling remains an escape hatch and does not replace normal lifecycle cleanup.

## Implementation Plan

1. Add failing server tests:
   - `/admin/clients/{clientId}/stream/stop` stops the selected client's stream through the streaming backend.
   - `/admin/clients/{clientId}/stream/stop` returns a clear `404` when the client has no session plan.
   - `/admin/clients/{clientId}/display/remove` restores physical primary and removes the selected client's virtual display lease.
2. Add failing cockpit tests:
   - the API client posts to the new admin routes;
   - the view model delegates selected-client stop-stream and remove-lease actions.
3. Implement server endpoints by reusing `InMemorySessionStore`, `IStreamingBackend`, and `DisplayLeaseManager`.
4. Add cockpit API/viewmodel commands and Recovery tab buttons.
5. Update README recovery examples and validation notes.

## Non-Goals

- UAC elevation support.
- Persistent diagnostic log aggregation.
- Real streaming protocol implementation.
- New Android emergency actions.
- Time-based cleanup or cancellation.

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

Commit and push the plan first. Then implement the red-green changes, validate locally, open a draft PR, wait for GitHub checks, mark ready, and merge if green.
