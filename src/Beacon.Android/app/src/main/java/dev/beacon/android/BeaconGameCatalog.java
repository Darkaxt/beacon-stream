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
