# Milestone 32 Android Game Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the Android APK fetch and show the server-owned game catalog without giving the APK any display-topology or launch-policy authority.

**Architecture:** Add a thin `GET /games` client method, a pure-Java catalog formatter, and a `BeaconViewModel.loadGames()` action. Keep the Activity simple: one button displays the formatted server catalog in the existing status surface while the game id field remains the launch selector.

**Tech Stack:** Android Java, Gson, JVM unit tests.

---

### Task 1: API And ViewModel Game Catalog

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconApiClient.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`
- Create: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconGameCatalog.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconApiClientTest.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconViewModelTest.java`

- [x] **Step 1: Write failing API and ViewModel tests**

Add to `BeaconApiClientTest`:

```java
@Test
public void gamesFetchesServerCatalogWithGet() throws Exception {
    FakeTransport transport = new FakeTransport();
    transport.response = new BeaconHttpResponse(200, "{\"games\":[]}");
    BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);

    BeaconApiClient.BeaconResult result = client.games();

    assertEquals(200, result.statusCode());
    assertEquals("GET", transport.method);
    assertEquals("/games", transport.path);
    assertEquals(null, transport.body);
    assertTrue(result.body().contains("\"games\""));
}
```

Add to `BeaconViewModelTest`:

```java
@Test
public void loadGamesFormatsServerCatalog() throws Exception {
    FakeService service = new FakeService();
    service.next = new BeaconApiClient.BeaconResult(
        200,
        "{\"games\":[{\"id\":\"steam-shortcut:3767414131\",\"title\":\"Dispatch\",\"source\":\"steam-shortcut\",\"installed\":true}],\"diagnostics\":[]}");
    BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

    model.loadGames();

    assertEquals("games", service.lastAction);
    assertEquals("games: 200", model.status());
    assertTrue(model.latestGames().contains("Dispatch"));
    assertTrue(model.latestGames().contains("steam-shortcut:3767414131"));
}
```

- [x] **Step 2: Run focused Android JVM tests to verify failure**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android :app:testDebugUnitTest --tests dev.beacon.android.BeaconApiClientTest --tests dev.beacon.android.BeaconViewModelTest
```

Expected: FAIL because `games()`, `loadGames()`, and `latestGames()` do not exist.

- [x] **Step 3: Implement game catalog boundary**

Add `BeaconApiClient.games()` using `GET /games`. Add `games()` to `BeaconViewModel.BeaconService`, implement `BeaconViewModel.loadGames()`, and add `latestGames()`.

Create `BeaconGameCatalog.summarize(String json)`:

```java
package dev.beacon.android;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.util.ArrayList;
import java.util.List;

public final class BeaconGameCatalog {
    private BeaconGameCatalog() {
    }

    public static String summarize(String json) {
        if (json == null || json.trim().isEmpty()) {
            return "";
        }

        JsonObject root = JsonParser.parseString(json).getAsJsonObject();
        JsonArray games = root.getAsJsonArray("games");
        if (games == null || games.size() == 0) {
            return "No games.";
        }

        List<String> lines = new ArrayList<>();
        for (JsonElement element : games) {
            JsonObject game = element.getAsJsonObject();
            String title = readString(game, "title");
            String id = readString(game, "id");
            String source = readString(game, "source");
            boolean installed = game.has("installed") && game.get("installed").getAsBoolean();
            lines.add(title + " [" + source + "] " + id + (installed ? " installed" : " not installed"));
        }

        return String.join("\n", lines);
    }

    private static String readString(JsonObject object, String name) {
        return object.has(name) && !object.get(name).isJsonNull() ? object.get(name).getAsString() : "";
    }
}
```

- [x] **Step 4: Run focused Android JVM tests**

Run the same focused Gradle command. Expected: PASS.

### Task 2: Activity, Docs, And Validation

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-32-android-game-catalog.md`

- [x] **Step 1: Wire Activity button**

Add a `Load Games` button near `Hello / Refresh`:

```java
root.addView(button("Load Games", model -> model.loadGames()));
```

Include games in the status body:

```java
setStatus(model.status() + "\nGames: " + model.latestGames() + "\nPlan: " + model.latestPlan() + "\nStream: " + model.latestStream());
```

- [x] **Step 2: Document Android catalog support**

Update README to mention Milestone 32 and that `Beacon.Android` can fetch/show the server-owned game catalog.

- [x] **Step 3: Run full validation**

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
```

Expected: all build/test/lint commands pass. The timeout-pattern audit should return no matches.

- [ ] **Step 4: Commit and sync**

Commit, push, open a pull request, wait for CI, and merge only after CI is green.
