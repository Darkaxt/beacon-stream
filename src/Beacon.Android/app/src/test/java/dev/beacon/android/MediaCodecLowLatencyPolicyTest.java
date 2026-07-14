package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class MediaCodecLowLatencyPolicyTest {
    @Test
    public void enablesOnlyWhenAndroidAndDecoderBothSupportLowLatency() {
        assertFalse(MediaCodecLowLatencyPolicy.shouldEnable(29, true));
        assertFalse(MediaCodecLowLatencyPolicy.shouldEnable(30, false));
        assertTrue(MediaCodecLowLatencyPolicy.shouldEnable(30, true));
        assertTrue(MediaCodecLowLatencyPolicy.shouldEnable(35, true));
    }
}
