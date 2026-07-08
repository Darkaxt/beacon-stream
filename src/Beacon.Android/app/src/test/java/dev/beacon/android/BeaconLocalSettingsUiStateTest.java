package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class BeaconLocalSettingsUiStateTest {
    @Test
    public void localInputFlagsAndDenseLayoutAreExposed() {
        BeaconLocalSettings settings = new BeaconLocalSettings();
        settings.touchLayout = "edge";
        settings.multitouchEnabled = false;
        settings.controllerOverlayEnabled = false;
        settings.hapticsEnabled = false;
        settings.uiDensity = "dense";

        BeaconLocalSettingsUiState state = BeaconLocalSettingsUiState.from(settings, false);
        BeaconLocalSettingsUiState defaultState = BeaconLocalSettingsUiState.from(new BeaconLocalSettings(), false);

        assertEquals("edge", state.touchLayout());
        assertEquals("dense", state.uiDensity());
        assertFalse(state.multitouchEnabled());
        assertFalse(state.controllerOverlayEnabled());
        assertFalse(state.hapticsEnabled());
        assertTrue(state.contentPaddingPx() < defaultState.contentPaddingPx());
        assertTrue(state.titleTextSizeSp() < defaultState.titleTextSizeSp());
        assertTrue(state.bodyTextSizeSp() < defaultState.bodyTextSizeSp());
        assertTrue(state.touchSurfaceMinHeightPx() < defaultState.touchSurfaceMinHeightPx());
    }

    @Test
    public void largeDensityUsesLargerMetricsThanDense() {
        BeaconLocalSettings denseSettings = new BeaconLocalSettings();
        denseSettings.uiDensity = "dense";
        BeaconLocalSettings largeSettings = new BeaconLocalSettings();
        largeSettings.uiDensity = "large";

        BeaconLocalSettingsUiState dense = BeaconLocalSettingsUiState.from(denseSettings, false);
        BeaconLocalSettingsUiState large = BeaconLocalSettingsUiState.from(largeSettings, false);

        assertTrue(large.contentPaddingPx() > dense.contentPaddingPx());
        assertTrue(large.titleTextSizeSp() > dense.titleTextSizeSp());
        assertTrue(large.bodyTextSizeSp() > dense.bodyTextSizeSp());
        assertTrue(large.touchSurfaceMinHeightPx() > dense.touchSurfaceMinHeightPx());
    }

    @Test
    public void darkSettingsEnableWakeLockDebugOverlayAndDarkPalette() {
        BeaconLocalSettings settings = new BeaconLocalSettings();
        settings.localTheme = "dark";
        settings.wakeLockEnabled = true;
        settings.decoderDebugOverlayEnabled = true;

        BeaconLocalSettingsUiState state = BeaconLocalSettingsUiState.from(settings, false);

        assertTrue(state.keepScreenAwake());
        assertTrue(state.debugOverlayVisible());
        assertTrue(state.darkTheme());
        assertEquals(0xff101418, state.backgroundColor());
        assertEquals(0xffffffff, state.textColor());
        assertEquals(0xff202a34, state.surfaceColor());
    }

    @Test
    public void lightSettingsDisableWakeLockDebugOverlayAndUseLightPalette() {
        BeaconLocalSettings settings = new BeaconLocalSettings();
        settings.localTheme = "light";
        settings.wakeLockEnabled = false;
        settings.decoderDebugOverlayEnabled = false;

        BeaconLocalSettingsUiState state = BeaconLocalSettingsUiState.from(settings, true);

        assertFalse(state.keepScreenAwake());
        assertFalse(state.debugOverlayVisible());
        assertFalse(state.darkTheme());
        assertEquals(0xfff5f7fa, state.backgroundColor());
        assertEquals(0xff14181c, state.textColor());
        assertEquals(0xffe1e7ee, state.surfaceColor());
    }

    @Test
    public void systemThemeFollowsSystemDarkModeAndNullSettingsUseDefaults() {
        BeaconLocalSettingsUiState state = BeaconLocalSettingsUiState.from(null, true);

        assertTrue(state.keepScreenAwake());
        assertFalse(state.debugOverlayVisible());
        assertTrue(state.darkTheme());
        assertEquals(0xff101418, state.backgroundColor());
    }
}
