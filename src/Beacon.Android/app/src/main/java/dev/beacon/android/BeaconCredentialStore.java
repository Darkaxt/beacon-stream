package dev.beacon.android;

public interface BeaconCredentialStore {
    String loadCredential();

    void saveCredential(String credential);

    void clearCredential();
}
