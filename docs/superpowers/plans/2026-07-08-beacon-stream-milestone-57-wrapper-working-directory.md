# Milestone 57: Wrapper Working Directory

Make external wrapper startup deterministic by launching wrapper processes with the executable directory as `WorkingDirectory`.

## Requirements

- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-TEST-007`: streaming integration remains behind a fakeable boundary.
- `REQ-REC-008`: wrapper failures should be diagnosable rather than caused by hidden host-process assumptions.
- `REQ-SYNC-006`: docs, implementation, and validation status must stay aligned.

## Implementation

- Extract `WindowsExternalStreamingProcessRunner.CreateStartInfo`.
- Set `WorkingDirectory` to `Path.GetDirectoryName(command.FileName)` when the wrapper path includes a directory.
- Preserve `UseShellExecute=false`, stdout/stderr capture, arguments, and Beacon environment variables.
- Add a platform test proving the start info uses the wrapper directory and keeps output capture enabled.

## Validation

- Focused platform test: `WindowsRunnerCreatesStartInfoWithWrapperWorkingDirectory`.
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
