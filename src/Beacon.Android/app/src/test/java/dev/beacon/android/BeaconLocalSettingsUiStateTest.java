package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class BeaconLocalSettingsUiStateTest {
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
