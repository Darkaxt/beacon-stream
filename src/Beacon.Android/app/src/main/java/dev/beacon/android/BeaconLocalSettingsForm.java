package dev.beacon.android;

public final class BeaconLocalSettingsForm {
    private BeaconLocalSettingsForm() {
    }

    public static BeaconLocalSettings update(
        BeaconLocalSettings existing,
        String touchLayout,
        boolean multitouchEnabled,
        boolean controllerOverlayEnabled,
        boolean hapticsEnabled,
        String uiDensity,
        String localTheme,
        boolean wakeLockEnabled,
        boolean decoderDebugOverlayEnabled) {
        BeaconLocalSettings value = copy(existing == null ? new BeaconLocalSettings() : existing);
        value.touchLayout = normalizeTouchLayout(touchLayout);
        value.multitouchEnabled = multitouchEnabled;
        value.controllerOverlayEnabled = controllerOverlayEnabled;
        value.hapticsEnabled = hapticsEnabled;
        value.uiDensity = normalizeUiDensity(uiDensity);
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

    private static String normalizeTouchLayout(String touchLayout) {
        if ("compact".equalsIgnoreCase(touchLayout)) {
            return "compact";
        }

        if ("edge".equalsIgnoreCase(touchLayout)) {
            return "edge";
        }

        return "default";
    }

    private static String normalizeUiDensity(String uiDensity) {
        if ("dense".equalsIgnoreCase(uiDensity)) {
            return "dense";
        }

        if ("large".equalsIgnoreCase(uiDensity)) {
            return "large";
        }

        return "comfortable";
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
