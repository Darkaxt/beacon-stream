# Milestone 76: Wrapper Argument Template Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Beacon pass a server-owned external streaming wrapper argument template into the Windows process runner.

**Architecture:** Keep the existing `BEACON_*` environment contract as the canonical data boundary, then add optional command-line templating for wrappers that need deterministic CLI arguments. Unknown template tokens fail preflight before display or app side effects, while unset templates preserve the existing default `--session` / `--display` / `--stream-session-descriptor` behavior.

**Tech Stack:** C#/.NET, xUnit, ASP.NET Core configuration, existing Windows external-process streaming adapter.

---

## Requirements

- `REQ-NET-006`: streaming endpoint and wrapper details remain server-owned.
- `REQ-NET-008`: invalid external-process configuration fails preflight before display or app side effects.
- `REQ-NET-009`: admin/Cockpit health must expose relevant wrapper readiness.
- `REQ-REC-010`: diagnostics expose wrapper configuration clearly enough to debug without phone testing.
- `REQ-SYNC-006`: docs, implementation, and validation status stay aligned.

## Files

- Modify: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`
- Modify: `tests/Beacon.Server.Tests/BeaconServiceRegistrationTests.cs`
- Modify: `README.md`
- Modify: `docs/external-streaming-wrapper-manifest.md`

## Tasks

### Task 1: External process option and command expansion

- [x] **Step 1: Write failing command-template tests**

Add tests that construct `ExternalProcessStreamingOptions(ArgumentTemplate: "--session {sessionId} --client {clientId} --app {appId} --display {displayId} --codec {codec} --fps {fps} --bitrate {bitrateMbps} --transport {transport} --descriptor {sessionDescriptorPath}")`, then assert the generated command expands each value from `SessionPlan`.

- [x] **Step 2: Run focused platform tests and confirm RED**

Run: `dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackendTests`

Expected: compile or assertion failure because `ArgumentTemplate` does not exist.

- [x] **Step 3: Implement minimal option and expansion**

Add `ArgumentTemplate` to `ExternalProcessStreamingOptions`, expand known tokens inside `CreateStartCommand`, and keep existing default arguments when the template is absent.

- [x] **Step 4: Run focused platform tests and confirm GREEN**

Run: `dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackendTests`

Expected: PASS.

### Task 2: Invalid template preflight

- [x] **Step 1: Write failing unknown-token preflight test**

Add a test proving `CheckReadinessAsync` fails with a useful diagnostic when `ArgumentTemplate` contains an unknown placeholder such as `{sunshineConfig}`.

- [x] **Step 2: Run focused test and confirm RED**

Run: `dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter Unknown`

Expected: FAIL because unknown tokens are not validated.

- [x] **Step 3: Implement template validation**

Reject unknown `{token}` values during readiness checks. Known tokens are `sessionId`, `clientId`, `appId`, `displayId`, `codec`, `fps`, `bitrateMbps`, `transport`, `sessionDescriptorPath`, `manifestPath`, `connectionProtocol`, `connectionLaunchUri`, `wrapperChildExecutablePath`, and `wrapperChildArguments`.

- [x] **Step 4: Run focused tests and confirm GREEN**

Run: `dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackendTests`

Expected: PASS.

### Task 3: Server configuration binding

- [x] **Step 1: Write failing registration tests**

Add configuration and environment override tests for `Beacon:Streaming:ExternalProcess:ArgumentTemplate` and `BEACON_EXTERNAL_STREAMING_ARGUMENT_TEMPLATE`.

- [x] **Step 2: Run server registration tests and confirm RED**

Run: `dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter BeaconServiceRegistrationTests`

Expected: compile or assertion failure because the setting is not bound.

- [x] **Step 3: Implement registration binding**

Add configuration and environment constants, thread the value through `AddBeaconServices`, and populate `ExternalProcessStreamingOptions.ArgumentTemplate`.

- [x] **Step 4: Run server registration tests and confirm GREEN**

Run: `dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter BeaconServiceRegistrationTests`

Expected: PASS.

### Task 4: Docs and validation

- [x] **Step 1: Document the wrapper argument template**

Update README and `docs/external-streaming-wrapper-manifest.md` with the setting names, token list, and the rule that invalid tokens fail before display/app side effects.

- [x] **Step 2: Run static validation**

Run `git diff --check`, the no-timeout audit, `dotnet format Beacon.slnx --verify-no-changes`, and `dotnet build Beacon.slnx -warnaserror`.

- [x] **Step 3: Run dynamic validation**

Run `dotnet test Beacon.slnx --no-build`, Client Lab lint/test/Playwright, Android `test assembleDebug`, and `dotnet run --project src/Beacon.DisplayProbe -- status`.

- [ ] **Step 4: Sync**

Commit, push, open a PR, verify CI, and merge if green.
