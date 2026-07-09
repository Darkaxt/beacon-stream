package dev.beacon.android;

import java.net.URI;
import java.net.URISyntaxException;
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
    private final Map<String, String> metadata;

    private GameStreamEndpointPlan(
        String protocol,
        boolean supportedProtocol,
        Map<String, String> endpoints,
        Map<String, String> metadata) {
        this.protocol = protocol;
        this.supportedProtocol = supportedProtocol;
        this.endpoints = endpoints;
        this.metadata = metadata;
    }

    public static GameStreamEndpointPlan from(StreamConnectionDescriptor descriptor) {
        if (descriptor == null || !descriptor.present()) {
            return new GameStreamEndpointPlan("", false, new LinkedHashMap<>(), new LinkedHashMap<>());
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

        return new GameStreamEndpointPlan(protocol, supportedProtocol, endpoints, new LinkedHashMap<>(descriptor.metadata()));
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

    public boolean rtspReady() {
        return rtspDiagnostic().isEmpty();
    }

    public String rtspUri() {
        return endpointUri("rtsp");
    }

    public String videoUri() {
        return endpointUri("video");
    }

    public String metadataValue(String key) {
        if (key == null || key.trim().isEmpty()) {
            return "";
        }

        return metadata.getOrDefault(key.trim(), "");
    }

    public String rtspHost() {
        URI parsed = parseRtspUriOrNull();
        if (parsed == null || parsed.getHost() == null) {
            return "";
        }

        return parsed.getHost();
    }

    public int rtspPort() {
        URI parsed = parseRtspUriOrNull();
        if (parsed == null) {
            return -1;
        }

        return parsed.getPort();
    }

    public String rtspPath() {
        URI parsed = parseRtspUriOrNull();
        if (parsed == null || parsed.getRawPath() == null || parsed.getRawPath().isEmpty()) {
            return "/";
        }

        return parsed.getRawPath();
    }

    public String rtspHostHeader() {
        String host = rtspHost();
        int port = rtspPort();
        if (host.isEmpty()) {
            return "";
        }

        if (port > 0) {
            return host + ":" + port;
        }

        return host;
    }

    public String rtspDiagnostic() {
        if (!supportedProtocol) {
            return "GameStream protocol is not supported for RTSP. protocol=" + protocol;
        }

        if (!complete()) {
            return "GameStream endpoint map is incomplete. Missing required endpoints: " + missingRequiredRoles();
        }

        String rtspUri = rtspUri();
        URI parsed;
        try {
            parsed = new URI(rtspUri);
        } catch (URISyntaxException ex) {
            return "GameStream RTSP endpoint is not a valid URI. rtsp=" + rtspUri;
        }

        String scheme = parsed.getScheme() == null ? "" : parsed.getScheme().toLowerCase(Locale.ROOT);
        if (!"rtsp".equals(scheme)) {
            return "GameStream RTSP endpoint must use rtsp://. rtsp=" + rtspUri;
        }

        if (parsed.getHost() == null || parsed.getHost().isEmpty()) {
            return "GameStream RTSP endpoint is missing a host. rtsp=" + rtspUri;
        }

        if (parsed.getPort() <= 0) {
            return "GameStream RTSP endpoint is missing a port. rtsp=" + rtspUri;
        }

        return "";
    }

    private String endpointUri(String role) {
        return endpoints.getOrDefault(role, "");
    }

    private URI parseRtspUriOrNull() {
        try {
            return new URI(rtspUri());
        } catch (URISyntaxException ex) {
            return null;
        }
    }
}
