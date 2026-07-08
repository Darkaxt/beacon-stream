# Milestone 77: Android Native Stream Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Android APK an in-app native stream-session boundary that can be exercised on an emulator before real decoder work.

**Architecture:** Keep existing `launchUri` delegation as the preferred path when the server provides one. When a successful launch response has a `stream.connection` descriptor without `launchUri`, route it to an injected `NativeStreamClient`; only explicitly supported protocols may start in-app, and unsupported protocols keep the visible endpoint-only diagnostic.

**Tech Stack:** Java Android app, JUnit JVM tests, Gradle Android build, ADB emulator smoke.

---

## Requirements

- `REQ-CTRL-013`: endpoint-only descriptors must not silently do nothing.
- `REQ-TEST-001`: most development and validation must not require the real phone.
- `REQ-TEST-010`: real phone testing remains final confirmation for decoder/input quality.
- `REQ-M0-001`: this does not implement the full real streaming backend.
- `REQ-SYNC-006`: docs, implementation, and validation status stay aligned.

## Files

- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/NativeStreamClient.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/NativeStreamStartResult.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/DiagnosticNativeStreamClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconViewModelTest.java`
- Add tests as needed under `src/Beacon.Android/app/src/test/java/dev/beacon/android`
- Modify: `README.md`

## Tasks

### Task 1: Native stream model and ViewModel routing

- [x] **Step 1: Write failing ViewModel tests**

Add tests proving a successful endpoint-only `beacon-test` descriptor starts the native stream client, updates a visible native stream status, and does not call the external URI launcher. Keep the existing unsupported endpoint-only `gamestream` diagnostic test green.

- [x] **Step 2: Run Android JVM tests and confirm RED**

Run the Gradle `test` task. Expected: compile failure because `NativeStreamClient` and `latestNativeStream` do not exist.

- [x] **Step 3: Implement minimal native stream boundary**

Add `NativeStreamClient`, `NativeStreamStartResult`, and ViewModel routing. Expose `latestNativeStream()` for UI/tests. Preserve existing `launchUri` behavior.

- [x] **Step 4: Run Android JVM tests and confirm GREEN**

Run the Gradle `test` task. Expected: PASS.

### Task 2: Diagnostic native stream client

- [x] **Step 1: Write failing client tests**

Add tests for `DiagnosticNativeStreamClient`: `beacon-test` succeeds with a deterministic status string; `gamestream` is unsupported and returns the descriptor diagnostic.

- [x] **Step 2: Run Android JVM tests and confirm RED**

Run the Gradle `test` task. Expected: fail until the implementation exists.

- [x] **Step 3: Implement diagnostic client**

Support only `beacon-test` in this milestone. Do not claim GameStream decode support. Use endpoint summary in the status for emulator/debug visibility.

- [x] **Step 4: Run Android JVM tests and confirm GREEN**

Run the Gradle `test` task. Expected: PASS.

### Task 3: APK UI and emulator smoke

- [x] **Step 1: Surface native stream status in the Activity**

Add native stream status to the existing status text after launch actions.

- [x] **Step 2: Update docs**

Document that the APK now has an in-app native stream boundary for `beacon-test` only, while GameStream/native decode remains future work.

- [x] **Step 3: Validate statically and dynamically**

Run diff check, timeout-pattern audit, .NET format/build/test, Client Lab checks, Android `test assembleDebug`, and display probe status. If an emulator is available, install and launch the APK on `emulator-5554` with ADB and record the result.

- [ ] **Step 4: Sync**

Commit, push, open PR, verify CI, and merge if green.
