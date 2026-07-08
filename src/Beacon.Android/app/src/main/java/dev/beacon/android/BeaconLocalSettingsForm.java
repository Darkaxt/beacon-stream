package dev.beacon.android;

public final class BeaconLocalSettingsForm {
    private BeaconLocalSettingsForm() {
    }

    public static BeaconLocalSettings update(
        BeaconLocalSettings existing,
        String localTheme,
        boolean wakeLockEnabled,
        boolean decoderDebugOverlayEnabled) {
        BeaconLocalSettings value = copy(existing == null ? new BeaconLocalSettings() : existing);
        value.localTheme = normalizeTheme(localTheme);
        value.wakeLockEnabled = wakeLockEnabled;
        value.decoderDebugOverlayEnabled = decoderDebugOverlayEnabled;
        return value;
    }

    private static BeaconLocalSettings copy(BeaconLocalSettings source) {
        BeaconLocalSettings copy = new BeaconLocalSettings();
        copy.touchLayout = source.touchLayout;
        copy.multitouchEnabled = source.multitouchEnabled;
        copy.controllerOverlayEnabled = source.controllerOverlayEnabled;
        copy.hapticsEnabled = source.hapticsEnabled;
        copy.uiDensity = source.uiDensity;
        copy.localTheme = source.localTheme;
        copy.wakeLockEnabled = source.wakeLockEnabled;
        copy.decoderDebugOverlayEnabled = source.decoderDebugOverlayEnabled;
        return copy;
    }

    private static String normalizeTheme(String localTheme) {
        if ("light".equalsIgnoreCase(localTheme)) {
            return "light";
        }

        if ("dark".equalsIgnoreCase(localTheme)) {
            return "dark";
        }

        return "system";
    }
}
