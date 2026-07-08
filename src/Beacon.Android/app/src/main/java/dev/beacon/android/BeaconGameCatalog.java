package dev.beacon.android;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class BeaconGameCatalog {
    private BeaconGameCatalog() {
    }

    public static String summarize(String json) {
        List<GameEntry> games = parse(json);
        if (games.isEmpty()) {
            return "No games.";
        }

        List<String> lines = new ArrayList<>();
        for (GameEntry game : games) {
            lines.add(game.displayLabel());
        }

        return String.join("\n", lines);
    }

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

    private static String readString(JsonObject object, String name) {
        return object.has(name) && !object.get(name).isJsonNull() ? object.get(name).getAsString() : "";
    }

    public static final class GameEntry {
        private final String id;
        private final String title;
        private final String source;
        private final boolean installed;
        private final String launchType;

        public GameEntry(String id, String title, String source, boolean installed, String launchType) {
            this.id = id;
            this.title = title;
            this.source = source;
            this.installed = installed;
            this.launchType = launchType;
        }

        public String id() {
            return id;
        }

        public String title() {
            return title;
        }

        public String source() {
            return source;
        }

        public boolean installed() {
            return installed;
        }

        public String launchType() {
            return launchType;
        }

        public String displayLabel() {
            return title + " [" + source + "] " + id + (installed ? " installed" : " not installed");
        }
    }
}
