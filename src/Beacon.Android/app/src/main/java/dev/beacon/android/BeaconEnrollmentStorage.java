package dev.beacon.android;

public interface BeaconEnrollmentStorage {
    String read();

    void write(String json);
}
