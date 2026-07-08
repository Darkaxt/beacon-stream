# Milestone 34 Android Catalog Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the Android APK use a loaded server game-catalog entry as the plan/launch target while keeping the raw game id field as a fallback.

**Architecture:** Extend `BeaconGameCatalog` from a string summarizer into a tiny parser that returns typed `GameEntry` values. `BeaconViewModel.loadGames()` stores those parsed entries for the Activity, and `BeaconActivity` displays them in a `Spinner`; plan/launch reads the selected catalog entry when available, otherwise it keeps using the raw `Game ID` text.

**Tech Stack:** Java 17, Android SDK widgets, Gson, JUnit 4.

---

### Task 1: Parsed Android Game Catalog

**Files:**
- Create: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconGameCatalogTest.java`
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconGameCatalog.java`

- [x] **Step 1: Write failing parser tests**

Create `BeaconGameCatalogTest` with these tests:

```java
package dev.beacon.android;

import org.junit.Test;

import java.util.List;

import static org.junit.Assert.assertEquals;

public final class BeaconGameCatalogTest {
    @Test
    public void parseReturnsTypedGameEntries() {
        List<BeaconGameCatalog.GameEntry> games = BeaconGameCatalog.parse(
            "{\"games\":["
                + "{\"id\":\"steam-shortcut:3767414131\",\"title\":\"Dispatch\",\"source\":\"steam-shortcut\",\"installed\":true,"
                + "\"launch\":{\"type\":\"steam-rungameid\",\"command\":\"steam://rungameid/16180920483166814208\"}},"
                + "{\"id\":\"heroic:persona-5\",\"title\":\"Persona 5\",\"source\":\"heroic\",\"installed\":false,"
                + "\"launch\":{\"type\":\"process\",\"command\":\"D:/Games/P5/p5.exe\"}}"
                + "]}");

        assertEquals(2, games.size());
        assertEquals("steam-shortcut:3767414131", games.get(0).id());
        assertEquals("Dispatch", games.get(0).title());
        assertEquals("steam-shortcut", games.get(0).source());
        assertEquals("steam-rungameid", games.get(0).launchType());
        assertEquals("Dispatch [steam-shortcut] steam-shortcut:3767414131 installed", games.get(0).displayLabel());
        assertEquals("Persona 5 [heroic] heroic:persona-5 not installed", games.get(1).displayLabel());
    }

    @Test
    public void parseEmptyCatalogReturnsEmptyList() {
        assertEquals(0, BeaconGameCatalog.parse("{\"games\":[]}").size());
    }

    @Test
    public void summarizeUsesParsedEntries() {
        String summary = BeaconGameCatalog.summarize(
            "{\"games\":[{\"id\":\"manual:game\",\"title\":\"Manual Game\",\"source\":\"manual\",\"installed\":true}]}");

        assertEquals("Manual Game [manual] manual:game installed", summary);
    }
}
```

- [x] **Step 2: Run focused tests to verify failure**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.BeaconGameCatalogTest
```

Expected: FAIL because `BeaconGameCatalog.parse` and `GameEntry` do not exist.

- [x] **Step 3: Implement typed catalog parsing**

Update `BeaconGameCatalog`:

```java
public static List<GameEntry> parse(String json) {
    if (json == null || json.trim().isEmpty()) {
        return Collections.emptyList();
    }

    JsonObject root = JsonParser.parseString(json).getAsJsonObject();
    JsonArray games = root.getAsJsonArray("games");
    if (games == null || games.size() == 0) {
        return Collections.emptyList();
    }

    List<GameEntry> entries = new ArrayList<>();
    for (JsonElement element : games) {
        JsonObject game = element.getAsJsonObject();
        JsonObject launch = game.has("launch") && game.get("launch").isJsonObject()
            ? game.getAsJsonObject("launch")
            : null;
        entries.add(new GameEntry(
            readString(game, "id"),
            readString(game, "title"),
            readString(game, "source"),
            game.has("installed") && game.get("installed").getAsBoolean(),
            launch == null ? "" : readString(launch, "type")));
    }

    return Collections.unmodifiableList(entries);
}
```

Add `GameEntry` as a static final nested value class with `id()`, `title()`, `source()`, `installed()`, `launchType()`, and `displayLabel()`.

Update `summarize` to call `parse(json)` and join `GameEntry.displayLabel()`.

- [x] **Step 4: Run focused tests to verify pass**

Run the same Gradle focused test. Expected: PASS.

### Task 2: ViewModel Catalog State

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconViewModel.java`
- Modify: `src/Beacon.Android/app/src/test/java/dev/beacon/android/BeaconViewModelTest.java`

- [x] **Step 1: Write failing ViewModel test**

Add this test to `BeaconViewModelTest`:

```java
@Test
public void loadGamesStoresParsedCatalogEntries() throws Exception {
    FakeService service = new FakeService();
    service.next = new BeaconApiClient.BeaconResult(
        200,
        "{\"games\":[{\"id\":\"steam-shortcut:3767414131\",\"title\":\"Dispatch\",\"source\":\"steam-shortcut\",\"installed\":true}]}");
    BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

    model.loadGames();

    assertEquals(1, model.latestGameEntries().size());
    assertEquals("steam-shortcut:3767414131", model.latestGameEntries().get(0).id());
    assertEquals("Dispatch [steam-shortcut] steam-shortcut:3767414131 installed", model.latestGameEntries().get(0).displayLabel());
}
```

- [x] **Step 2: Run focused test to verify failure**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android testDebugUnitTest --tests dev.beacon.android.BeaconViewModelTest.loadGamesStoresParsedCatalogEntries
```

Expected: FAIL because `latestGameEntries()` does not exist.

- [x] **Step 3: Store parsed entries after successful catalog load**

Add a `List<BeaconGameCatalog.GameEntry> latestGameEntries` field initialized to `Collections.emptyList()`, expose it through `latestGameEntries()`, and update `loadGames()`:

```java
if (result.isSuccess()) {
    latestGameEntries = BeaconGameCatalog.parse(result.body());
    latestGames = BeaconGameCatalog.summarize(result.body());
} else {
    latestGameEntries = Collections.emptyList();
    latestGames = "";
}
```

- [x] **Step 4: Run focused test to verify pass**

Run the same focused Gradle test. Expected: PASS.

### Task 3: Activity Game Selector

**Files:**
- Modify: `src/Beacon.Android/app/src/main/java/dev/beacon/android/BeaconActivity.java`

- [x] **Step 1: Wire a spinner without removing the raw fallback**

Add fields:

```java
private Spinner gameSelector;
private ArrayAdapter<String> gameAdapter;
private final List<BeaconGameCatalog.GameEntry> gameEntries = new ArrayList<>();
```

Initialize the adapter in `createContent()` after `gameId`:

```java
gameSelector = new Spinner(this);
gameAdapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, new ArrayList<>());
gameAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
gameSelector.setAdapter(gameAdapter);
```

Add the spinner to the layout after the raw `gameId` field.

- [x] **Step 2: Populate the selector from Load Games**

Change the `Load Games` button action from:

```java
root.addView(button("Load Games", model -> model.loadGames()));
```

to:

```java
root.addView(button("Load Games", model -> {
    model.loadGames();
    setGameEntries(model.latestGameEntries());
}));
```

Add `setGameEntries`:

```java
private void setGameEntries(List<BeaconGameCatalog.GameEntry> entries) {
    runOnUiThread(() -> {
        gameEntries.clear();
        gameEntries.addAll(entries);
        gameAdapter.clear();
        for (BeaconGameCatalog.GameEntry entry : gameEntries) {
            gameAdapter.add(entry.displayLabel());
        }
        gameAdapter.notifyDataSetChanged();
        if (!gameEntries.isEmpty()) {
            gameId.setText(gameEntries.get(0).id());
        }
    });
}
```

- [x] **Step 3: Use selected catalog entry for plan and launch**

Update `readGame()`:

```java
int index = gameSelector == null ? -1 : gameSelector.getSelectedItemPosition();
if (index >= 0 && index < gameEntries.size()) {
    return BeaconApiClient.GameSelection.byGameId(gameEntries.get(index).id());
}

return BeaconApiClient.GameSelection.byGameId(textValue(gameId));
```

- [x] **Step 4: Run Android compile/test gate**

Run:

```powershell
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: PASS.

### Task 4: Validation And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-34-android-catalog-selection.md`

- [x] **Step 1: Document the milestone**

Update README Android client summary to mention that Android can load the server catalog and use a selected catalog entry for plan/launch.

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
