package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class BeaconLocalSettingsFormTest {
    @Test
    public void updateAppliesAllClientLocalSettings() {
        BeaconLocalSettings existing = new BeaconLocalSettings();

        BeaconLocalSettings updated = BeaconLocalSettingsForm.update(
            existing,
            "edge",
            false,
            false,
            false,
            "dense",
            "light",
            false,
            true);

        assertEquals("edge", updated.touchLayout);
        assertFalse(updated.multitouchEnabled);
        assertFalse(updated.controllerOverlayEnabled);
        assertFalse(updated.hapticsEnabled);
        assertEquals("dense", updated.uiDensity);
        assertEquals("light", updated.localTheme);
        assertFalse(updated.wakeLockEnabled);
        assertTrue(updated.decoderDebugOverlayEnabled);
        assertFalse(updated.toJson().contains("displayMode"));
        assertFalse(updated.toJson().contains("blackout"));
        assertFalse(updated.toJson().contains("restorePhysicalDisplayOnEnd"));
    }

    @Test
    public void invalidLocalSettingValuesFallBackToDefaults() {
        BeaconLocalSettings updated = BeaconLocalSettingsForm.update(
            null,
            "unsupported-layout",
            true,
            true,
            true,
            "unsupported-density",
            "unsupported-theme",
            true,
            false);

        assertEquals("default", updated.touchLayout);
        assertEquals("comfortable", updated.uiDensity);
        assertEquals("system", updated.localTheme);
    }

    @Test
    public void updatePreservesOtherClientLocalSettings() {
        BeaconLocalSettings existing = new BeaconLocalSettings();
        existing.touchLayout = "compact";
        existing.multitouchEnabled = false;
        existing.controllerOverlayEnabled = false;
        existing.hapticsEnabled = false;
        existing.uiDensity = "dense";

        BeaconLocalSettings updated = BeaconLocalSettingsForm.update(
            existing,
            existing.touchLayout,
            existing.multitouchEnabled,
            existing.controllerOverlayEnabled,
            existing.hapticsEnabled,
            existing.uiDensity,
            "light",
            false,
            true);

        assertEquals("compact", updated.touchLayout);
        assertFalse(updated.multitouchEnabled);
        assertFalse(updated.controllerOverlayEnabled);
        assertFalse(updated.hapticsEnabled);
        assertEquals("dense", updated.uiDensity);
        assertEquals("light", updated.localTheme);
        assertFalse(updated.wakeLockEnabled);
        assertTrue(updated.decoderDebugOverlayEnabled);
        assertFalse(updated.toJson().contains("displayMode"));
    }

    @Test
    public void invalidThemeFallsBackToSystem() {
        BeaconLocalSettings updated = BeaconLocalSettingsForm.update(
            null,
            "default",
            true,
            true,
            true,
            "comfortable",
            "unsupported",
            true,
            false);

        assertEquals("system", updated.localTheme);
    }
}
