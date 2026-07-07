package dev.beacon.android;

public final class BeaconLocalSettings {
    public String touchLayout = "default";
    public boolean multitouchEnabled = true;
    public boolean controllerOverlayEnabled = true;
    public boolean hapticsEnabled = true;
    public String uiDensity = "comfortable";
    public String localTheme = "system";
    public boolean wakeLockEnabled = true;
    public boolean decoderDebugOverlayEnabled = false;

    public String toJson() {
        return BeaconJson.gson().toJson(this);
    }

    public static BeaconLocalSettings fromJson(String json) {
        if (json == null || json.trim().isEmpty()) {
            return new BeaconLocalSettings();
        }

        return BeaconJson.gson().fromJson(json, BeaconLocalSettings.class);
    }
}
