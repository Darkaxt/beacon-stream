package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;

public final class BeaconTestPatternPaletteTest {
    @Test
    public void colorBarsPaletteHasDeterministicReadableBands() {
        int[] colors = BeaconTestPatternPalette.colorsFor("color-bars");

        assertEquals(6, colors.length);
        assertEquals(0xFFE53935, colors[0]);
        assertEquals(0xFFFFEB3B, colors[1]);
        assertEquals(0xFF43A047, colors[2]);
        assertEquals(0xFF1E88E5, colors[3]);
        assertEquals(0xFF8E24AA, colors[4]);
        assertEquals(0xFFFFFFFF, colors[5]);
    }

    @Test
    public void unknownPatternUsesNeutralFallback() {
        int[] colors = BeaconTestPatternPalette.colorsFor("unknown");

        assertEquals(2, colors.length);
        assertEquals(0xFF263238, colors[0]);
        assertEquals(0xFF607D8B, colors[1]);
    }
}
