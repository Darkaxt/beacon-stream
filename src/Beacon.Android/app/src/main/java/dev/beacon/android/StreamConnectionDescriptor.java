package dev.beacon.android;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParseException;
import com.google.gson.JsonParser;

import dev.beacon.streaming.moonlight.MoonlightNativeSessionPlan;

import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public final class StreamConnectionDescriptor {
    private static final StreamConnectionDescriptor EMPTY = new StreamConnectionDescriptor(
        false,
        "",
        "",
        Collections.emptyList(),
        Collections.emptyMap(),
        false,
        null,
        "");

    private final boolean present;
    private final String protocol;
    private final String launchUri;
    private final List<Endpoint> endpoints;
    private final Map<String, String> metadata;
    private final boolean nativeSessionProvided;
    private final MoonlightNativeSessionPlan nativeSession;
    private final String nativeSessionDiagnostic;

    private StreamConnectionDescriptor(
        boolean present,
        String protocol,
        String launchUri,
        List<Endpoint> endpoints,
        Map<String, String> metadata,
        boolean nativeSessionProvided,
        MoonlightNativeSessionPlan nativeSession,
        String nativeSessionDiagnostic) {
        this.present = present;
        this.protocol = protocol;
        this.launchUri = launchUri;
        this.endpoints = Collections.unmodifiableList(new ArrayList<>(endpoints));
        this.metadata = Collections.unmodifiableMap(new LinkedHashMap<>(metadata));
        this.nativeSessionProvided = nativeSessionProvided;
        this.nativeSession = nativeSession;
        this.nativeSessionDiagnostic = nativeSessionDiagnostic;
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

            String protocol = stringProperty(connection, "protocol");
            NativeSessionExtraction nativeSession = nativeSession(root, protocol);

            return new StreamConnectionDescriptor(
                true,
                protocol,
                stringProperty(connection, "launchUri"),
                endpoints(connection),
                metadata(connection),
                nativeSession.provided,
                nativeSession.plan,
                nativeSession.diagnostic);
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

    public Map<String, String> metadata() {
        return metadata;
    }

    public boolean nativeSessionProvided() {
        return nativeSessionProvided;
    }

    public boolean nativeSessionValid() {
        return nativeSession != null;
    }

    public MoonlightNativeSessionPlan nativeSession() {
        return nativeSession;
    }

    public String nativeSessionDiagnostic() {
        return nativeSessionDiagnostic;
    }

    public String metadataValue(String key) {
        if (key == null || key.trim().isEmpty()) {
            return "";
        }

        return metadata.getOrDefault(key.trim(), "");
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

    private static Map<String, String> metadata(JsonObject connection) {
        JsonObject metadata = objectProperty(connection, "metadata");
        if (metadata == null) {
            return Collections.emptyMap();
        }

        Map<String, String> result = new LinkedHashMap<>();
        for (Map.Entry<String, JsonElement> entry : metadata.entrySet()) {
            String key = entry.getKey() == null ? "" : entry.getKey().trim();
            JsonElement value = entry.getValue();
            if (key.isEmpty() || value == null || value.isJsonNull() || !value.isJsonPrimitive()) {
                continue;
            }

            String stringValue = value.getAsString().trim();
            if (!stringValue.isEmpty()) {
                result.put(key, stringValue);
            }
        }

        return result;
    }

    private static NativeSessionExtraction nativeSession(JsonObject root, String protocol) {
        if (!root.has("nativeSession") || root.get("nativeSession").isJsonNull()) {
            return NativeSessionExtraction.absent();
        }
        if (!root.get("nativeSession").isJsonObject()) {
            return NativeSessionExtraction.invalid("nativeSession must be an object.");
        }
        if (!"gamestream".equalsIgnoreCase(protocol)) {
            return NativeSessionExtraction.invalid(
                "nativeSession requires stream.connection.protocol=gamestream.");
        }

        JsonObject descriptor = root.getAsJsonObject("nativeSession");
        try {
            MoonlightNativeSessionPlan plan = MoonlightNativeSessionPlan.create(
                requiredString(descriptor, "address"),
                requiredString(descriptor, "serverAppVersion"),
                optionalString(descriptor, "serverGfeVersion"),
                requiredString(descriptor, "rtspSessionUrl"),
                requiredInt(descriptor, "serverCodecModeSupport"),
                requiredInt(descriptor, "width"),
                requiredInt(descriptor, "height"),
                requiredInt(descriptor, "fps"),
                requiredInt(descriptor, "bitrateKbps"),
                requiredInt(descriptor, "packetSize"),
                requiredString(descriptor, "streamingMode"),
                requiredString(descriptor, "audioConfiguration"),
                requiredString(descriptor, "videoFormat"),
                requiredInt(descriptor, "clientRefreshRateX100"),
                requiredString(descriptor, "colorSpace"),
                requiredString(descriptor, "colorRange"),
                requiredString(descriptor, "encryptionMode"),
                requiredString(descriptor, "remoteInputAesKey"),
                requiredString(descriptor, "remoteInputAesIv"));
            return NativeSessionExtraction.valid(plan);
        } catch (IllegalArgumentException ex) {
            return NativeSessionExtraction.invalid(ex.getMessage() == null
                ? "nativeSession is invalid."
                : ex.getMessage());
        }
    }

    private static String requiredString(JsonObject object, String propertyName) {
        if (!object.has(propertyName)
            || object.get(propertyName).isJsonNull()
            || !object.get(propertyName).isJsonPrimitive()
            || !object.getAsJsonPrimitive(propertyName).isString()) {
            throw new IllegalArgumentException(propertyName + " must be a string.");
        }

        String value = object.get(propertyName).getAsString().trim();
        if (value.isEmpty()) {
            throw new IllegalArgumentException(propertyName + " is required.");
        }
        return value;
    }

    private static String optionalString(JsonObject object, String propertyName) {
        if (!object.has(propertyName) || object.get(propertyName).isJsonNull()) {
            return null;
        }
        return requiredString(object, propertyName);
    }

    private static int requiredInt(JsonObject object, String propertyName) {
        if (!object.has(propertyName)
            || object.get(propertyName).isJsonNull()
            || !object.get(propertyName).isJsonPrimitive()
            || !object.getAsJsonPrimitive(propertyName).isNumber()) {
            throw new IllegalArgumentException(propertyName + " must be an integer.");
        }

        try {
            return new BigDecimal(object.get(propertyName).getAsString()).intValueExact();
        } catch (ArithmeticException | NumberFormatException ex) {
            throw new IllegalArgumentException(propertyName + " must be a 32-bit integer.");
        }
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

    private static final class NativeSessionExtraction {
        private final boolean provided;
        private final MoonlightNativeSessionPlan plan;
        private final String diagnostic;

        private NativeSessionExtraction(
            boolean provided,
            MoonlightNativeSessionPlan plan,
            String diagnostic) {
            this.provided = provided;
            this.plan = plan;
            this.diagnostic = diagnostic;
        }

        private static NativeSessionExtraction absent() {
            return new NativeSessionExtraction(false, null, "");
        }

        private static NativeSessionExtraction valid(MoonlightNativeSessionPlan plan) {
            return new NativeSessionExtraction(true, plan, "");
        }

        private static NativeSessionExtraction invalid(String diagnostic) {
            return new NativeSessionExtraction(true, null, diagnostic);
        }
    }
}
