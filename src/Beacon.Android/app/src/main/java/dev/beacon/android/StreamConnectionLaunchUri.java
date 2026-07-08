package dev.beacon.android;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParseException;
import com.google.gson.JsonParser;

public final class StreamConnectionLaunchUri {
    private StreamConnectionLaunchUri() {
    }

    public static String extract(String responseBody) {
        if (responseBody == null || responseBody.isBlank()) {
            return "";
        }

        try {
            JsonElement rootElement = JsonParser.parseString(responseBody);
            if (!rootElement.isJsonObject()) {
                return "";
            }

            JsonObject root = rootElement.getAsJsonObject();
            JsonObject stream = objectProperty(root, "stream");
            JsonObject connection = objectProperty(stream, "connection");
            if (connection == null || !connection.has("launchUri") || connection.get("launchUri").isJsonNull()) {
                return "";
            }

            return connection.get("launchUri").getAsString().trim();
        } catch (IllegalStateException | UnsupportedOperationException | JsonParseException ex) {
            return "";
        }
    }

    private static JsonObject objectProperty(JsonObject parent, String propertyName) {
        if (parent == null || !parent.has(propertyName) || !parent.get(propertyName).isJsonObject()) {
            return null;
        }

        return parent.getAsJsonObject(propertyName);
    }
}
