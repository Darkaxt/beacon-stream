package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class BeaconLocalSettingsStoreTest {
    @Test
    public void loadReturnsDefaultsWhenStorageIsEmpty() {
        FakeStorage storage = new FakeStorage();
        BeaconLocalSettingsStore store = new BeaconLocalSettingsStore(storage);

        BeaconLocalSettings settings = store.load();

        assertEquals("default", settings.touchLayout);
        assertTrue(settings.multitouchEnabled);
        assertTrue(settings.controllerOverlayEnabled);
        assertTrue(settings.hapticsEnabled);
        assertEquals("comfortable", settings.uiDensity);
        assertEquals("system", settings.localTheme);
        assertTrue(settings.wakeLockEnabled);
        assertFalse(settings.decoderDebugOverlayEnabled);
    }

    @Test
    public void saveWritesJsonAndLoadRestoresClientLocalSettings() {
        FakeStorage storage = new FakeStorage();
        BeaconLocalSettingsStore store = new BeaconLocalSettingsStore(storage);
        BeaconLocalSettings saved = new BeaconLocalSettings();
        saved.touchLayout = "compact";
        saved.multitouchEnabled = false;
        saved.controllerOverlayEnabled = false;
        saved.hapticsEnabled = false;
        saved.uiDensity = "dense";
        saved.localTheme = "dark";
        saved.wakeLockEnabled = false;
        saved.decoderDebugOverlayEnabled = true;

        store.save(saved);
        BeaconLocalSettings loaded = store.load();

        assertTrue(storage.json.contains("touchLayout"));
        assertFalse(storage.json.contains("displayMode"));
        assertEquals("compact", loaded.touchLayout);
        assertFalse(loaded.multitouchEnabled);
        assertFalse(loaded.controllerOverlayEnabled);
        assertFalse(loaded.hapticsEnabled);
        assertEquals("dense", loaded.uiDensity);
        assertEquals("dark", loaded.localTheme);
        assertFalse(loaded.wakeLockEnabled);
        assertTrue(loaded.decoderDebugOverlayEnabled);
    }

    @Test
    public void loadReturnsDefaultsWhenStoredJsonIsInvalid() {
        FakeStorage storage = new FakeStorage();
        storage.json = "{not-json";
        BeaconLocalSettingsStore store = new BeaconLocalSettingsStore(storage);

        BeaconLocalSettings settings = store.load();

        assertEquals("default", settings.touchLayout);
        assertTrue(settings.multitouchEnabled);
    }

    private static final class FakeStorage implements BeaconLocalSettingsStorage {
        String json = "";

        @Override
        public String read() {
            return json;
        }

        @Override
        public void write(String json) {
            this.json = json;
        }
    }
}
