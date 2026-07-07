package dev.beacon.android;

public final class BeaconClientConfig {
    private final String serverUrl;
    private final String clientId;

    public BeaconClientConfig(String serverUrl, String clientId) {
        this.serverUrl = normalizeServerUrl(serverUrl);
        this.clientId = requireText(clientId, "clientId");
    }

    public String serverUrl() {
        return serverUrl;
    }

    public String clientId() {
        return clientId;
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
}
