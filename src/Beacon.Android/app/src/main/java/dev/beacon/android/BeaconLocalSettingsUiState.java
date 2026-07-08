package dev.beacon.android;

public final class BeaconLocalSettingsUiState {
    private static final int DARK_BACKGROUND = 0xff101418;
    private static final int DARK_TEXT = 0xffffffff;
    private static final int DARK_HINT = 0xffa0aab4;
    private static final int DARK_SURFACE = 0xff202a34;

    private static final int LIGHT_BACKGROUND = 0xfff5f7fa;
    private static final int LIGHT_TEXT = 0xff14181c;
    private static final int LIGHT_HINT = 0xff647078;
    private static final int LIGHT_SURFACE = 0xffe1e7ee;

    private final boolean keepScreenAwake;
    private final boolean debugOverlayVisible;
    private final boolean darkTheme;

    private BeaconLocalSettingsUiState(
        boolean keepScreenAwake,
        boolean debugOverlayVisible,
        boolean darkTheme) {
        this.keepScreenAwake = keepScreenAwake;
        this.debugOverlayVisible = debugOverlayVisible;
        this.darkTheme = darkTheme;
    }

    public static BeaconLocalSettingsUiState from(BeaconLocalSettings settings, boolean systemDarkTheme) {
        BeaconLocalSettings value = settings == null ? new BeaconLocalSettings() : settings;
        return new BeaconLocalSettingsUiState(
            value.wakeLockEnabled,
            value.decoderDebugOverlayEnabled,
            resolveDarkTheme(value.localTheme, systemDarkTheme));
    }

    public boolean keepScreenAwake() {
        return keepScreenAwake;
    }

    public boolean debugOverlayVisible() {
        return debugOverlayVisible;
    }

    public boolean darkTheme() {
        return darkTheme;
    }

    public int backgroundColor() {
        return darkTheme ? DARK_BACKGROUND : LIGHT_BACKGROUND;
    }

    public int textColor() {
        return darkTheme ? DARK_TEXT : LIGHT_TEXT;
    }

    public int hintColor() {
        return darkTheme ? DARK_HINT : LIGHT_HINT;
    }

    public int surfaceColor() {
        return darkTheme ? DARK_SURFACE : LIGHT_SURFACE;
    }

    private static boolean resolveDarkTheme(String localTheme, boolean systemDarkTheme) {
        if ("dark".equalsIgnoreCase(localTheme)) {
            return true;
        }

        if ("light".equalsIgnoreCase(localTheme)) {
            return false;
        }

        return systemDarkTheme;
    }
}
