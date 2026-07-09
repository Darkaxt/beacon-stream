package dev.beacon.android;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

public final class GameStreamEndpointPlan {
    private static final String[] RequiredRoles = {"rtsp", "video", "control", "audio"};

    private final String protocol;
    private final boolean supportedProtocol;
    private final Map<String, String> endpoints;

    private GameStreamEndpointPlan(String protocol, boolean supportedProtocol, Map<String, String> endpoints) {
        this.protocol = protocol;
        this.supportedProtocol = supportedProtocol;
        this.endpoints = endpoints;
    }

    public static GameStreamEndpointPlan from(StreamConnectionDescriptor descriptor) {
        if (descriptor == null || !descriptor.present()) {
            return new GameStreamEndpointPlan("", false, new LinkedHashMap<>());
        }

        String protocol = descriptor.protocol().trim().toLowerCase(Locale.ROOT);
        boolean supportedProtocol = "gamestream".equals(protocol) || "moonlight".equals(protocol);
        Map<String, String> endpoints = new LinkedHashMap<>();
        for (StreamConnectionDescriptor.Endpoint endpoint : descriptor.endpoints()) {
            String role = endpoint.role().trim().toLowerCase(Locale.ROOT);
            if (!role.isEmpty() && !endpoints.containsKey(role)) {
                endpoints.put(role, endpoint.uri());
            }
        }

        return new GameStreamEndpointPlan(protocol, supportedProtocol, endpoints);
    }

    public boolean supportedProtocol() {
        return supportedProtocol;
    }

    public boolean complete() {
        if (!supportedProtocol) {
            return false;
        }

        for (String role : RequiredRoles) {
            if (!endpoints.containsKey(role)) {
                return false;
            }
        }

        return true;
    }

    public String protocol() {
        return protocol;
    }

    public String missingRequiredRoles() {
        if (!supportedProtocol) {
            return String.join(", ", RequiredRoles);
        }

        List<String> missing = new ArrayList<>();
        for (String role : RequiredRoles) {
            if (!endpoints.containsKey(role)) {
                missing.add(role);
            }
        }

        return String.join(", ", missing);
    }

    public String requiredEndpointSummary() {
        List<String> values = new ArrayList<>();
        for (String role : RequiredRoles) {
            String uri = endpoints.get(role);
            if (uri != null && !uri.isEmpty()) {
                values.add(role + "=" + uri);
            }
        }

        return String.join(", ", values);
    }

    public String diagnosticEndpointSummary() {
        if (endpoints.isEmpty()) {
            return "none";
        }

        List<String> values = new ArrayList<>();
        for (Map.Entry<String, String> endpoint : endpoints.entrySet()) {
            values.add(endpoint.getKey() + "=" + endpoint.getValue());
        }

        return String.join(", ", values);
    }
}
