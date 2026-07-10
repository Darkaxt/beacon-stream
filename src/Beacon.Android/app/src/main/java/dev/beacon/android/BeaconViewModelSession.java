package dev.beacon.android;

public final class BeaconViewModelSession {
    private final Factory factory;
    private BeaconViewModel activeModel;
    private String activeClientId = "";
    private String activeServerUrl = "";

    public BeaconViewModelSession(Factory factory) {
        if (factory == null) {
            throw new IllegalArgumentException("BeaconViewModelSession factory is required.");
        }

        this.factory = factory;
    }

    public synchronized BeaconViewModel get(String clientId, String serverUrl) {
        String safeClientId = clientId == null ? "" : clientId;
        String safeServerUrl = serverUrl == null ? "" : serverUrl;
        if (activeModel != null &&
            activeClientId.equals(safeClientId) &&
            activeServerUrl.equals(safeServerUrl)) {
            return activeModel;
        }

        close();
        activeClientId = safeClientId;
        activeServerUrl = safeServerUrl;
        activeModel = factory.create(safeClientId, safeServerUrl);
        return activeModel;
    }

    public synchronized void close() {
        activeModel = null;
        activeClientId = "";
        activeServerUrl = "";
    }

    public interface Factory {
        BeaconViewModel create(String clientId, String serverUrl);
    }
}
