package dev.beacon.android;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParseException;
import com.google.gson.JsonParser;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class StreamConnectionDescriptor {
    private static final StreamConnectionDescriptor EMPTY = new StreamConnectionDescriptor(false, "", "", Collections.emptyList());

    private final boolean present;
    private final String protocol;
    private final String launchUri;
    private final List<Endpoint> endpoints;

    private StreamConnectionDescriptor(boolean present, String protocol, String launchUri, List<Endpoint> endpoints) {
        this.present = present;
        this.protocol = protocol;
        this.launchUri = launchUri;
        this.endpoints = Collections.unmodifiableList(new ArrayList<>(endpoints));
    }

    public static StreamConnectionDescriptor extract(String responseBody) {
        if (responseBody == null || responseBody.isBlank()) {
            return EMPTY;
        }

        try {
            JsonElement rootElement = JsonParser.parseString(responseBody);
            if (!rootElement.isJsonObject()) {
                return EMPTY;
            }

            JsonObject root = rootElement.getAsJsonObject();
            JsonObject stream = objectProperty(root, "stream");
            JsonObject connection = objectProperty(stream, "connection");
            if (connection == null) {
                return EMPTY;
            }

            return new StreamConnectionDescriptor(
                true,
                stringProperty(connection, "protocol"),
                stringProperty(connection, "launchUri"),
                endpoints(connection));
        } catch (IllegalStateException | UnsupportedOperationException | JsonParseException ex) {
            return EMPTY;
        }
    }

    public boolean present() {
        return present;
    }

    public String protocol() {
        return protocol;
    }

    public String launchUri() {
        return launchUri;
    }

    public List<Endpoint> endpoints() {
        return endpoints;
    }

    public String endpointSummary() {
        List<String> values = new ArrayList<>();
        for (Endpoint endpoint : endpoints) {
            values.add(endpoint.role() + "=" + endpoint.uri());
        }

        return String.join(", ", values);
    }

    public String missingLaunchUriDiagnostic() {
        if (!present || !launchUri.isEmpty()) {
            return "";
        }

        String protocolPart = protocol.isEmpty() ? "protocol=unknown" : "protocol=" + protocol;
        String endpointPart = endpointSummary().isEmpty() ? "endpoints=none" : "endpoints=" + endpointSummary();
        return "Stream connection did not include a launch URI. " + protocolPart + " " + endpointPart;
    }

    private static JsonObject objectProperty(JsonObject parent, String propertyName) {
        if (parent == null || !parent.has(propertyName) || !parent.get(propertyName).isJsonObject()) {
            return null;
        }

        return parent.getAsJsonObject(propertyName);
    }

    private static String stringProperty(JsonObject parent, String propertyName) {
        if (parent == null || !parent.has(propertyName) || parent.get(propertyName).isJsonNull()) {
            return "";
        }

        return parent.get(propertyName).getAsString().trim();
    }

    private static List<Endpoint> endpoints(JsonObject connection) {
        if (!connection.has("endpoints") || !connection.get("endpoints").isJsonArray()) {
            return Collections.emptyList();
        }

        JsonArray endpoints = connection.getAsJsonArray("endpoints");
        List<Endpoint> result = new ArrayList<>();
        for (JsonElement endpointElement : endpoints) {
            if (!endpointElement.isJsonObject()) {
                continue;
            }

            JsonObject endpoint = endpointElement.getAsJsonObject();
            String role = stringProperty(endpoint, "role");
            String uri = stringProperty(endpoint, "uri");
            if (!role.isEmpty() && !uri.isEmpty()) {
                result.add(new Endpoint(role, uri));
            }
        }

        return result;
    }

    public static final class Endpoint {
        private final String role;
        private final String uri;

        private Endpoint(String role, String uri) {
            this.role = role;
            this.uri = uri;
        }

        public String role() {
            return role;
        }

        public String uri() {
            return uri;
        }
    }
}
