package dev.beacon.android;

public interface BeaconLocalSettingsStorage {
    String read();

    void write(String json);
}
