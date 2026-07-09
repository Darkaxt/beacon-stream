package dev.beacon.android;

public final class BeaconTestPatternPalette {
    private static final int[] ColorBars = new int[] {
        0xFFE53935,
        0xFFFFEB3B,
        0xFF43A047,
        0xFF1E88E5,
        0xFF8E24AA,
        0xFFFFFFFF
    };

    private static final int[] Fallback = new int[] {
        0xFF263238,
        0xFF607D8B
    };

    private BeaconTestPatternPalette() {
    }

    public static int[] colorsFor(String pattern) {
        int[] source = "color-bars".equalsIgnoreCase(pattern) ? ColorBars : Fallback;
        int[] copy = new int[source.length];
        System.arraycopy(source, 0, copy, 0, source.length);
        return copy;
    }
}
