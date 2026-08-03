package dev.beacon.android;

import java.util.concurrent.Executor;

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
        return get(new BeaconClientConfig(serverUrl, clientId));
    }

    public synchronized BeaconViewModel get(BeaconClientConfig config) {
        if (config == null) {
            throw new IllegalArgumentException("Beacon client config is required.");
        }
        if (isCurrent(config.clientId(), config.serverUrl())) {
            return activeModel;
        }

        releaseActiveModel();
        BeaconViewModel created = factory.create(config);
        activeClientId = config.clientId();
        activeServerUrl = config.serverUrl();
        activeModel = created;
        return activeModel;
    }

    public void execute(Executor executor, BeaconClientConfig config, ModelAction action) {
        if (executor == null || action == null) {
            throw new IllegalArgumentException("Beacon model executor and action are required.");
        }

        executor.execute(() -> action.run(get(config)));
    }

    public synchronized BeaconViewModel current(BeaconClientConfig config) {
        return config != null && isCurrent(config.clientId(), config.serverUrl())
            ? activeModel
            : null;
    }

    public synchronized boolean isCurrent(String clientId, String serverUrl) {
        return activeModel != null &&
            activeClientId.equals(clientId == null ? "" : clientId) &&
            activeServerUrl.equals(serverUrl == null ? "" : serverUrl);
    }

    public synchronized void close() {
        releaseActiveModel();
    }

    private void releaseActiveModel() {
        BeaconViewModel model = activeModel;
        activeModel = null;
        activeClientId = "";
        activeServerUrl = "";
        if (model == null) return;

        model.setForegroundDesired(false);
        try {
            model.reconcileBackground();
        } catch (Exception ignored) {
            // Cleanup steps are independent because the old identity cannot be revisited safely.
        }
        try {
            model.quit(new BeaconApiClient.QuitState(false));
        } catch (Exception ignored) {
            // Closing local resources must still run after a failed server quit.
        }
        try {
            model.close();
        } catch (RuntimeException ignored) {
            // Replacement must not retain a partially closed model.
        }
    }

    public interface Factory {
        BeaconViewModel create(BeaconClientConfig config);
    }

    public interface ModelAction {
        void run(BeaconViewModel model);
    }
}
