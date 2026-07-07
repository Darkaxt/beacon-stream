package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class BeaconLocalSettingsTest {
    @Test
    public void localSettingsContainOnlyClientInteractionPreferences() {
        BeaconLocalSettings settings = new BeaconLocalSettings();
        settings.touchLayout = "compact";
        settings.multitouchEnabled = true;
        settings.controllerOverlayEnabled = false;
        settings.hapticsEnabled = true;
        settings.localTheme = "system";
        settings.wakeLockEnabled = true;

        String json = settings.toJson();

        assertTrue(json.contains("touchLayout"));
        assertTrue(json.contains("multitouchEnabled"));
        assertTrue(json.contains("controllerOverlayEnabled"));
        assertTrue(json.contains("hapticsEnabled"));
        assertTrue(json.contains("localTheme"));
        assertTrue(json.contains("wakeLockEnabled"));
        assertFalse(json.contains("displayMode"));
        assertFalse(json.contains("blackout"));
        assertFalse(json.contains("mirror"));
        assertFalse(json.contains("restorePhysicalDisplayOnEnd"));
        assertFalse(json.contains("virtualDisplayPersistence"));
    }
}
