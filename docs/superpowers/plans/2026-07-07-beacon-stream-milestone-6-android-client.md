# Milestone 6: Thin Android Client

## Objective

Add the first real `Beacon.Android` APK shell without making the phone the source of truth. The app identifies a known client, fetches and patches only that client's allowed server-side profile fields, reports capabilities and telemetry, requests a server plan, starts/stops the server-side stream lifecycle, and can trigger emergency restore for its own client.

Real video decode, native input forwarding, pairing/auth hardening, and Moonlight/Sunshine protocol work are out of scope for this milestone.

## Reference Boundary

- Mature Android clients such as Moonlight prove that the phone-side application can own local interaction preferences while the server owns host/session setup. Beacon follows that split but keeps the initial APK smaller.
- Beacon already has the server control-plane endpoints needed for this slice: hello, profile, patch, capabilities, telemetry, plan, launch, stream status, stream stop, disconnect, reconnect, quit, and emergency restore.
- Do not copy Android client source from Moonlight, Artemis, or any other project in this milestone.

## Requirements Covered

- `REQ-CTRL-002`: APK reports identity, facts, capabilities, preferences, telemetry, and user intent.
- `REQ-CTRL-003`: APK can update its own basic server-side client profile before launch.
- `REQ-CTRL-004`: APK does not edit global server settings.
- `REQ-CTRL-005`: APK does not edit per-game virtual desktop behavior.
- `REQ-CTRL-006`: client-local input/UI settings stay on the client.
- `REQ-CTRL-007`: virtual desktop behavior remains server-side state.
- `REQ-CTRL-009`: APK consumes the server-computed session plan.
- `REQ-CTRL-011`: profile patches stay inside the server allowlist.
- `REQ-PROFILE-007`: local touch/UI/decoder preferences stay local.
- `REQ-REC-005`: APK can call emergency actions for its own active session.
- `REQ-TEST-001`: most validation does not require the real phone.
- `REQ-TEST-010`: real phone testing remains final confirmation.

## Toolchain Decision

Use a minimal Java Android app with the Android Gradle Plugin. Kotlin is deliberately skipped in this milestone because the local cache has AGP and Java/JUnit pieces available but no Kotlin plugin cache. This keeps the first APK build small and avoids installing a broader toolchain before there is a need.

The existing cached Gradle distribution can validate the app:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

## Task 1: Plan And Sync

Files:

- Add: `docs/superpowers/plans/2026-07-07-beacon-stream-milestone-6-android-client.md`

- [x] **Step 1: Create milestone branch**

```powershell
git switch -c codex/milestone-6-android-client
```

- [x] **Step 2: Commit plan**

```powershell
git add docs/superpowers/plans/2026-07-07-beacon-stream-milestone-6-android-client.md
git commit -m "Plan Android client milestone"
git push -u origin codex/milestone-6-android-client
```

## Task 2: Scaffold Android Project

Files:

- Add: `src/Beacon.Android/settings.gradle`
- Add: `src/Beacon.Android/build.gradle`
- Add: `src/Beacon.Android/app/build.gradle`
- Add: `src/Beacon.Android/app/src/main/AndroidManifest.xml`
- Add: `src/Beacon.Android/app/src/main/res/values/strings.xml`
- Add: `src/Beacon.Android/app/src/main/res/values/colors.xml`
- Add: `src/Beacon.Android/app/src/main/res/mipmap-anydpi-v26/ic_launcher.xml`
- Add: `src/Beacon.Android/app/src/main/res/drawable/ic_launcher_foreground.xml`
- Add: `src/Beacon.Android/app/src/main/res/drawable/ic_launcher_background.xml`

- [x] **Step 1: Create build files**

Use `com.android.application` version `8.9.2`, compile SDK `35`, min SDK `26`, Java 17 compatibility, and JUnit `4.13.2`.

- [x] **Step 2: Create manifest and resources**

Declare `BeaconActivity` as the launcher activity. Use app label `Beacon`.

- [x] **Step 3: Verify Android test and debug build**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test
```

Expected: pass once activity and app code exist.

## Task 3: Add APK Control-Plane Client

Files:

- Add: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconApiClient.java`
- Add: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconClientConfig.java`
- Add: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconLocalSettings.java`
- Add: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconJson.java`
- Add: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconApiClientTest.java`
- Add: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconLocalSettingsTest.java`

- [x] **Step 1: Add API client tests**

Use an in-memory `HttpURLConnection`-style fake transport or an injectable transport interface. Tests must cover:

- hello request sends only client identity.
- profile patch serializes only APK-allowed fields.
- emergency restore posts to the owning client endpoint.
- launch consumes a server plan/stream response without choosing display topology locally.

- [x] **Step 2: Implement API client**

Implement methods:

- `hello()`
- `patchProfile(ProfilePatch patch)`
- `reportCapabilities(ClientCapabilities capabilities)`
- `reportTelemetry(ClientTelemetry telemetry)`
- `requestPlan(GameSelection game)`
- `launch(GameSelection game)`
- `stopStream()`
- `disconnect()`
- `quit(QuitState state)`
- `emergencyRestore()`

- [x] **Step 3: Add local settings model**

Persist only local interaction/UI settings:

- touch layout
- multitouch enabled
- controller overlay enabled
- haptics enabled
- UI density
- local theme
- wake lock enabled
- decoder debug overlay enabled

Do not include display mode, blackout, mirror, persistence, destruction, or restore policy in local settings.

- [x] **Step 4: Verify unit tests**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test
```

Expected: all Android JVM tests pass.

- [x] **Step 5: Commit**

```powershell
git add src/Beacon.Android docs/superpowers/plans/2026-07-07-beacon-stream-milestone-6-android-client.md
git commit -m "Add Android control-plane client"
git push
```

## Task 4: Add Minimal APK UI

Files:

- Add: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
- Add: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`
- Add: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconViewModelTest.java`

- [x] **Step 1: Add ViewModel tests**

Cover:

- initial state shows configured client id and server URL.
- refresh calls hello/profile flow.
- patch action cannot send server-global or display-policy fields.
- emergency restore action calls the owning-client endpoint.
- launch action reports the server-selected plan and stream state.

- [x] **Step 2: Implement ViewModel**

Keep policy-free state only: connection status, profile summary, selected game id/manual launch fields, latest plan summary, latest stream state, and latest error.

- [x] **Step 3: Implement Activity**

Use platform Android views, no AndroidX dependency. Provide a functional first screen:

- server URL field
- client id field defaulting to `z-fold-7`
- hello/refresh button
- resolution/refresh/HDR/codec/quality/bitrate/audio basic profile fields
- report capabilities button
- report telemetry button
- plan button
- launch button
- stop stream button
- disconnect button
- quit button
- emergency restore button
- status log text

- [x] **Step 4: Verify APK build**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: tests pass and `app-debug.apk` is produced.

- [x] **Step 5: Commit**

```powershell
git add src/Beacon.Android docs/superpowers/plans/2026-07-07-beacon-stream-milestone-6-android-client.md
git commit -m "Add Android client shell"
git push
```

## Task 5: Client Lab And Docs Alignment

Files:

- Modify: `README.md`
- Modify: `docs/extraction-map.md`
- Modify: `docs/superpowers/plans/2026-07-07-beacon-stream-milestone-6-android-client.md`

- [ ] **Step 1: Update README**

Add Android build and validation commands. Note that the APK is a thin control-plane client and does not include real decode yet.

- [ ] **Step 2: Update extraction map**

Add that Android client code is original and does not copy Artemis/Moonlight code.

- [ ] **Step 3: Full validation**

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

Expected: all checks pass.

- [ ] **Step 4: Boundary audit**

Run:

```powershell
rg "Moonlight|Artemis|Sunshine|Apollo" src/Beacon.Android -n
rg "Thread\.sleep|Handler\.postDelayed|timeout|Timeout|Timer|CountDownTimer" src/Beacon.Android -n
```

Expected:

- No copied-source references.
- No timeout/cancellation patterns in APK code.

- [ ] **Step 5: Commit docs and validation**

```powershell
git add README.md docs/extraction-map.md docs/superpowers/plans/2026-07-07-beacon-stream-milestone-6-android-client.md
git commit -m "Document Android client milestone"
git push
```

- [ ] **Step 6: Open PR, wait for CI, mark ready, merge**

```powershell
gh pr create --draft --base main --head codex/milestone-6-android-client --title "Add thin Android client" --body "Milestone 6 thin Android client implementation."
gh pr checks --watch
gh pr ready
gh pr merge --merge --delete-branch
```

## Out Of Scope

- Real video decoder or Moonlight protocol implementation.
- Input forwarding to the streaming backend.
- Pairing/auth hardening beyond the local trusted-client assumption.
- AndroidX/Compose UI.
- Global server settings.
- Per-game display topology settings.
- Display lifecycle policy in the APK.
- Any hard timeout-driven cancellation behavior.
