package dev.beacon.android;

public final class BeaconLocalSettingsStore {
    private final BeaconLocalSettingsStorage storage;

    public BeaconLocalSettingsStore(BeaconLocalSettingsStorage storage) {
        this.storage = storage;
    }

    public BeaconLocalSettings load() {
        try {
            BeaconLocalSettings settings = BeaconLocalSettings.fromJson(storage.read());
            return settings == null ? new BeaconLocalSettings() : settings;
        } catch (RuntimeException ex) {
            return new BeaconLocalSettings();
        }
    }

    public void save(BeaconLocalSettings settings) {
        BeaconLocalSettings value = settings == null ? new BeaconLocalSettings() : settings;
        storage.write(value.toJson());
    }
}
