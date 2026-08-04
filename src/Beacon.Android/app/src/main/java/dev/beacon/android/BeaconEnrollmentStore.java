package dev.beacon.android;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

public final class BeaconEnrollmentStore {
    private final BeaconEnrollmentStorage storage;

    public BeaconEnrollmentStore(BeaconEnrollmentStorage storage) {
        if (storage == null) {
            throw new IllegalArgumentException("Beacon enrollment storage is required.");
        }
        this.storage = storage;
    }

    public BeaconClientConfig load() {
        try {
            String saved = storage.read();
            if (saved == null || saved.trim().isEmpty()) return null;

            JsonObject json = JsonParser.parseString(saved).getAsJsonObject();
            return new BeaconClientConfig(
                json.get("serverUrl").getAsString(),
                json.get("clientId").getAsString(),
                json.get("publicKeyFingerprint").getAsString());
        } catch (RuntimeException invalid) {
            return null;
        }
    }

    public void save(BeaconClientConfig config) {
        if (config == null || config.testHost()) {
            throw new IllegalArgumentException("A production Beacon client configuration is required.");
        }

        JsonObject json = new JsonObject();
        json.addProperty("serverUrl", config.serverUrl());
        json.addProperty("clientId", config.clientId());
        json.addProperty("publicKeyFingerprint", config.publicKeyFingerprint());
        storage.write(json.toString());
    }
}
