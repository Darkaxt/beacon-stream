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

    @Test
    public void exactHdr10OutputFormatMatchesRequestedMetadata() {
        EncodedVideoDecodeRequest request = hdrRequest();
        EncodedVideoOutputFormat output = new EncodedVideoOutputFormat(
            "video/hevc",
            android.media.MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10,
            android.media.MediaFormat.COLOR_STANDARD_BT2020,
            android.media.MediaFormat.COLOR_TRANSFER_ST2084,
            android.media.MediaFormat.COLOR_RANGE_LIMITED,
            sequence(25));

        assertTrue(AndroidMediaCodecFactory.outputFormatMatches(request, output));
    }

    @Test
    public void genericMain10OrWrongStaticMetadataCannotCertifyHdr10Output() {
        EncodedVideoDecodeRequest request = hdrRequest();
        assertFalse(AndroidMediaCodecFactory.outputFormatMatches(
            request,
            new EncodedVideoOutputFormat(
                "video/hevc",
                android.media.MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10,
                android.media.MediaFormat.COLOR_STANDARD_BT2020,
                android.media.MediaFormat.COLOR_TRANSFER_ST2084,
                android.media.MediaFormat.COLOR_RANGE_LIMITED,
                sequence(25))));
        byte[] changed = sequence(25);
        changed[24] = 99;
        assertFalse(AndroidMediaCodecFactory.outputFormatMatches(
            request,
            new EncodedVideoOutputFormat(
                "video/hevc",
                android.media.MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10,
                android.media.MediaFormat.COLOR_STANDARD_BT2020,
                android.media.MediaFormat.COLOR_TRANSFER_ST2084,
                android.media.MediaFormat.COLOR_RANGE_LIMITED,
                changed)));
    }

    @Test
    public void omittedOutputProfileAndStaticInfoAreAllowedWithIndependentHdrFacts() {
        EncodedVideoDecodeRequest request = hdrRequest();
        EncodedVideoOutputFormat omitted = new EncodedVideoOutputFormat(
            "video/hevc",
            -1,
            android.media.MediaFormat.COLOR_STANDARD_BT2020,
            android.media.MediaFormat.COLOR_TRANSFER_ST2084,
            android.media.MediaFormat.COLOR_RANGE_LIMITED,
            new byte[0]);

        assertTrue(AndroidMediaCodecFactory.outputFormatMatches(
            request, omitted, true));
        assertFalse(AndroidMediaCodecFactory.outputFormatMatches(
            request, omitted, false));
    }

    @Test
    public void omittedStaticInfoStillRejectsStrictColorMismatch() {
        EncodedVideoDecodeRequest request = hdrRequest();
        assertFalse(AndroidMediaCodecFactory.outputFormatMatches(
            request,
            new EncodedVideoOutputFormat(
                "video/hevc",
                -1,
                android.media.MediaFormat.COLOR_STANDARD_BT2020,
                android.media.MediaFormat.COLOR_TRANSFER_HLG,
                android.media.MediaFormat.COLOR_RANGE_LIMITED,
                new byte[0]),
            true));
    }

    private static EncodedVideoDecodeRequest hdrRequest() {
        return new EncodedVideoDecodeRequest(
            "hevc", 3840, 2160, 60,
            "hevcMain10", 10, "hdr10", "bt2020", "pq",
            "bt2020NonConstantLuminance", "limited", sequence(25), true,
            new EncodedVideoSampleProvider() {
                @Override public EncodedVideoSample nextSample() {
                    return EncodedVideoSample.eos();
                }
                @Override public int maxSampleBytes() { return 0; }
            });
    }

    private static byte[] sequence(int count) {
        byte[] result = new byte[count];
        for (int index = 0; index < count; index++) result[index] = (byte) index;
        return result;
    }
}
