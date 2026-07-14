package dev.beacon.android;

import android.media.MediaCodec;

import org.junit.Test;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class AndroidMediaCodecFactoryTest {
    @Test
    public void codecConfigurationAndEmptyOutputDoNotRequireFrameIdentity() {
        assertFalse(AndroidMediaCodecFactory.outputRequiresFrameIdentity(
            32,
            MediaCodec.BUFFER_FLAG_CODEC_CONFIG));
        assertFalse(AndroidMediaCodecFactory.outputRequiresFrameIdentity(
            0,
            MediaCodec.BUFFER_FLAG_END_OF_STREAM));
        assertFalse(AndroidMediaCodecFactory.outputRequiresFrameIdentity(0, 0));
    }

    @Test
    public void framePayloadRequiresIdentityEvenWhenItCarriesEndOfStream() {
        assertTrue(AndroidMediaCodecFactory.outputRequiresFrameIdentity(32, 0));
        assertTrue(AndroidMediaCodecFactory.outputRequiresFrameIdentity(
            32,
            MediaCodec.BUFFER_FLAG_END_OF_STREAM));
    }
}
