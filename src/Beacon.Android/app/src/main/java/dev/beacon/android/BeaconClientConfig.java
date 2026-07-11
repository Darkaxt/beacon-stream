package dev.beacon.android;

public final class BeaconClientConfig {
    private final String serverUrl;
    private final String clientId;
    private final String publicKeyFingerprint;
    private final boolean testHost;

    BeaconClientConfig(String serverUrl, String clientId) {
        this.serverUrl = normalizeServerUrl(serverUrl);
        this.clientId = requireText(clientId, "clientId");
        this.publicKeyFingerprint = "";
        this.testHost = true;
    }

    public BeaconClientConfig(String serverUrl, String clientId, String publicKeyFingerprint) {
        this.serverUrl = normalizeServerUrl(serverUrl);
        this.clientId = requireText(clientId, "clientId");
        this.publicKeyFingerprint = requireFingerprint(publicKeyFingerprint);
        this.testHost = false;
        if (!this.serverUrl.regionMatches(true, 0, "https://", 0, 8)) {
            throw new IllegalArgumentException("Beacon production serverUrl must use HTTPS.");
        }
    }

    public String serverUrl() {
        return serverUrl;
    }

    public String clientId() {
        return clientId;
    }

    public String publicKeyFingerprint() {
        return publicKeyFingerprint;
    }

    public boolean testHost() {
        return testHost;
    }

    private static String normalizeServerUrl(String value) {
        String text = requireText(value, "serverUrl");
        while (text.endsWith("/")) {
            text = text.substring(0, text.length() - 1);
        }

        return text;
    }

    private static String requireText(String value, String name) {
        if (value == null || value.trim().isEmpty()) {
            throw new IllegalArgumentException(name + " is required.");
        }

        return value.trim();
    }

    private static String requireFingerprint(String value) {
        String text = requireText(value, "publicKeyFingerprint").replace(":", "").toUpperCase();
        if (!text.matches("[0-9A-F]{64}")) {
            throw new IllegalArgumentException("publicKeyFingerprint must contain 64 hexadecimal characters.");
        }
        return text;
    }
}
