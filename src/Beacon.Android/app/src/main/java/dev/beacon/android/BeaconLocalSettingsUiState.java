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
    private final boolean multitouchEnabled;
    private final boolean controllerOverlayEnabled;
    private final boolean hapticsEnabled;
    private final String touchLayout;
    private final String uiDensity;
    private final int contentPaddingPx;
    private final int titleTextSizeSp;
    private final int bodyTextSizeSp;
    private final int touchSurfaceMinHeightPx;

    private BeaconLocalSettingsUiState(
        boolean keepScreenAwake,
        boolean debugOverlayVisible,
        boolean darkTheme,
        boolean multitouchEnabled,
        boolean controllerOverlayEnabled,
        boolean hapticsEnabled,
        String touchLayout,
        String uiDensity,
        int contentPaddingPx,
        int titleTextSizeSp,
        int bodyTextSizeSp,
        int touchSurfaceMinHeightPx) {
        this.keepScreenAwake = keepScreenAwake;
        this.debugOverlayVisible = debugOverlayVisible;
        this.darkTheme = darkTheme;
        this.multitouchEnabled = multitouchEnabled;
        this.controllerOverlayEnabled = controllerOverlayEnabled;
        this.hapticsEnabled = hapticsEnabled;
        this.touchLayout = touchLayout;
        this.uiDensity = uiDensity;
        this.contentPaddingPx = contentPaddingPx;
        this.titleTextSizeSp = titleTextSizeSp;
        this.bodyTextSizeSp = bodyTextSizeSp;
        this.touchSurfaceMinHeightPx = touchSurfaceMinHeightPx;
    }

    public static BeaconLocalSettingsUiState from(BeaconLocalSettings settings, boolean systemDarkTheme) {
        BeaconLocalSettings value = settings == null ? new BeaconLocalSettings() : settings;
        String touchLayout = normalizeTouchLayout(value.touchLayout);
        String uiDensity = normalizeUiDensity(value.uiDensity);
        return new BeaconLocalSettingsUiState(
            value.wakeLockEnabled,
            value.decoderDebugOverlayEnabled,
            resolveDarkTheme(value.localTheme, systemDarkTheme),
            value.multitouchEnabled,
            value.controllerOverlayEnabled,
            value.hapticsEnabled,
            touchLayout,
            uiDensity,
            contentPaddingPx(uiDensity),
            titleTextSizeSp(uiDensity),
            bodyTextSizeSp(uiDensity),
            touchSurfaceMinHeightPx(uiDensity, touchLayout));
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

    public boolean multitouchEnabled() {
        return multitouchEnabled;
    }

    public boolean controllerOverlayEnabled() {
        return controllerOverlayEnabled;
    }

    public boolean hapticsEnabled() {
        return hapticsEnabled;
    }

    public String touchLayout() {
        return touchLayout;
    }

    public String uiDensity() {
        return uiDensity;
    }

    public int contentPaddingPx() {
        return contentPaddingPx;
    }

    public int titleTextSizeSp() {
        return titleTextSizeSp;
    }

    public int bodyTextSizeSp() {
        return bodyTextSizeSp;
    }

    public int touchSurfaceMinHeightPx() {
        return touchSurfaceMinHeightPx;
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

    private static int contentPaddingPx(String uiDensity) {
        if ("dense".equals(uiDensity)) {
            return 18;
        }

        if ("large".equals(uiDensity)) {
            return 36;
        }

        return 28;
    }

    private static int titleTextSizeSp(String uiDensity) {
        if ("dense".equals(uiDensity)) {
            return 24;
        }

        if ("large".equals(uiDensity)) {
            return 32;
        }

        return 28;
    }

    private static int bodyTextSizeSp(String uiDensity) {
        if ("dense".equals(uiDensity)) {
            return 13;
        }

        if ("large".equals(uiDensity)) {
            return 16;
        }

        return 14;
    }

    private static int touchSurfaceMinHeightPx(String uiDensity, String touchLayout) {
        int height;
        if ("dense".equals(uiDensity)) {
            height = 300;
        } else if ("large".equals(uiDensity)) {
            height = 440;
        } else {
            height = 360;
        }

        if ("compact".equals(touchLayout)) {
            height -= 60;
        } else if ("edge".equals(touchLayout)) {
            height += 40;
        }

        return Math.max(220, height);
    }
}
