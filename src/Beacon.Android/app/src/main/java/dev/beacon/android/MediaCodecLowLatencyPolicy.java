package dev.beacon.android;

final class MediaCodecLowLatencyPolicy {
    private static final int AndroidR = 30;

    private MediaCodecLowLatencyPolicy() { }

    static boolean shouldEnable(int androidApiLevel, boolean decoderSupportsLowLatency) {
        return androidApiLevel >= AndroidR && decoderSupportsLowLatency;
    }
}
