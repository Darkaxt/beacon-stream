package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class BeaconLocalSettingsFormTest {
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
            "unsupported",
            true,
            false);

        assertEquals("system", updated.localTheme);
    }
}
