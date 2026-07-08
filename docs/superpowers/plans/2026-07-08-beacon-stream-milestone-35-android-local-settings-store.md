# Milestone 35 Android Local Settings Store Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Android APK a real local persistence boundary for client-only interaction settings without adding server API surface or display-policy settings.

**Architecture:** Add a tiny `BeaconLocalSettingsStorage` abstraction so JVM tests can validate persistence without Android framework state. `BeaconLocalSettingsStore` owns defaulting and JSON roundtrip behavior, while `SharedPreferencesLocalSettingsStorage` is the Android adapter for runtime persistence.

**Tech Stack:** Java 17, Android SharedPreferences, Gson, JUnit 4.

---

### Task 1: Local Settings Store Contract

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconLocalSettingsStorage.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconLocalSettingsStore.java`
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconLocalSettingsStoreTest.java`

- [x] **Step 1: Write failing store tests**

Create `BeaconLocalSettingsStoreTest`:

```java
package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class BeaconLocalSettingsStoreTest {
    @Test
    public void loadReturnsDefaultsWhenStorageIsEmpty() {
        FakeStorage storage = new FakeStorage();
        BeaconLocalSettingsStore store = new BeaconLocalSettingsStore(storage);

        BeaconLocalSettings settings = store.load();

        assertEquals("default", settings.touchLayout);
        assertTrue(settings.multitouchEnabled);
        assertTrue(settings.controllerOverlayEnabled);
        assertTrue(settings.hapticsEnabled);
        assertEquals("comfortable", settings.uiDensity);
        assertEquals("system", settings.localTheme);
        assertTrue(settings.wakeLockEnabled);
        assertFalse(settings.decoderDebugOverlayEnabled);
    }

    @Test
    public void saveWritesJsonAndLoadRestoresClientLocalSettings() {
        FakeStorage storage = new FakeStorage();
        BeaconLocalSettingsStore store = new BeaconLocalSettingsStore(storage);
        BeaconLocalSettings saved = new BeaconLocalSettings();
        saved.touchLayout = "compact";
        saved.multitouchEnabled = false;
        saved.controllerOverlayEnabled = false;
        saved.hapticsEnabled = false;
        saved.uiDensity = "dense";
        saved.localTheme = "dark";
        saved.wakeLockEnabled = false;
        saved.decoderDebugOverlayEnabled = true;

        store.save(saved);
        BeaconLocalSettings loaded = store.load();

        assertTrue(storage.json.contains("touchLayout"));
        assertFalse(storage.json.contains("displayMode"));
        assertEquals("compact", loaded.touchLayout);
        assertFalse(loaded.multitouchEnabled);
        assertFalse(loaded.controllerOverlayEnabled);
        assertFalse(loaded.hapticsEnabled);
        assertEquals("dense", loaded.uiDensity);
        assertEquals("dark", loaded.localTheme);
        assertFalse(loaded.wakeLockEnabled);
        assertTrue(loaded.decoderDebugOverlayEnabled);
    }

    @Test
    public void loadReturnsDefaultsWhenStoredJsonIsInvalid() {
        FakeStorage storage = new FakeStorage();
        storage.json = "{not-json";
        BeaconLocalSettingsStore store = new BeaconLocalSettingsStore(storage);

        BeaconLocalSettings settings = store.load();

        assertEquals("default", settings.touchLayout);
        assertTrue(settings.multitouchEnabled);
    }

    private static final class FakeStorage implements BeaconLocalSettingsStorage {
        String json = "";

        @Override
        public String read() {
            return json;
        }

        @Override
        public void write(String json) {
            this.json = json;
        }
    }
}
```

- [x] **Step 2: Run focused test to verify failure**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.BeaconLocalSettingsStoreTest
```

Expected: FAIL because `BeaconLocalSettingsStore` and `BeaconLocalSettingsStorage` do not exist.

- [x] **Step 3: Implement pure store boundary**

Create `BeaconLocalSettingsStorage`:

```java
package dev.beacon.android;

public interface BeaconLocalSettingsStorage {
    String read();

    void write(String json);
}
```

Create `BeaconLocalSettingsStore`:

```java
package dev.beacon.android;

public final class BeaconLocalSettingsStore {
    private final BeaconLocalSettingsStorage storage;

    public BeaconLocalSettingsStore(BeaconLocalSettingsStorage storage) {
        this.storage = storage;
    }

    public BeaconLocalSettings load() {
        try {
            BeaconLocalSettings settings = BeaconLocalSettings.fromJson(storage.read());
            return settings == null ? new BeaconLocalSettings() : settings;
        } catch (RuntimeException ex) {
            return new BeaconLocalSettings();
        }
    }

    public void save(BeaconLocalSettings settings) {
        BeaconLocalSettings value = settings == null ? new BeaconLocalSettings() : settings;
        storage.write(value.toJson());
    }
}
```

- [x] **Step 4: Run focused test to verify pass**

Run the same focused Gradle test. Expected: PASS.

### Task 2: Android SharedPreferences Adapter

**Files:**
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/SharedPreferencesLocalSettingsStorage.java`

- [x] **Step 1: Add SharedPreferences adapter**

Create `SharedPreferencesLocalSettingsStorage`:

```java
package dev.beacon.android;

import android.content.SharedPreferences;

public final class SharedPreferencesLocalSettingsStorage implements BeaconLocalSettingsStorage {
    static final String KEY = "beacon.localSettings.v1";

    private final SharedPreferences preferences;

    public SharedPreferencesLocalSettingsStorage(SharedPreferences preferences) {
        this.preferences = preferences;
    }

    @Override
    public String read() {
        return preferences.getString(KEY, "");
    }

    @Override
    public void write(String json) {
        preferences.edit().putString(KEY, json == null ? "" : json).apply();
    }
}
```

- [x] **Step 2: Run Android compile/test gate**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: PASS.

### Task 3: Documentation, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-35-android-local-settings-store.md`

- [x] **Step 1: Document the milestone**

Update README to mention that Android local interaction settings now have a local persistence boundary and SharedPreferences adapter.

- [x] **Step 2: Run full validation**

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
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
git diff --check
```

Expected: all build/test/lint commands pass. The timeout-pattern audit returns no matches.

- [ ] **Step 3: Commit and sync**

Commit, push, open a pull request, wait for CI, and merge only after CI is green.
