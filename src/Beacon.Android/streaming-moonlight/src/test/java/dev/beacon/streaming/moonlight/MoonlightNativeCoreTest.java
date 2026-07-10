package dev.beacon.streaming.moonlight;

import org.junit.Test;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

public final class MoonlightNativeCoreTest {
    @Test
    public void reportsUnavailableOutsideAndroidNativeRuntime() {
        assertFalse(MoonlightNativeCore.isAvailable());
        assertTrue(MoonlightNativeCore.diagnostic().startsWith("Moonlight native core unavailable:"));
        assertThrows(IllegalStateException.class, MoonlightNativeCore::identity);
    }
}
