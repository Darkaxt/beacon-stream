package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class EncodedVideoDecodeRequestTest {
    @Test
    public void carriesExactHevcMain10Hdr10MetadataDefensively() {
        byte[] staticInfo = sequence(25);
        EncodedVideoDecodeRequest request = new EncodedVideoDecodeRequest(
            "hevc", 3840, 2160, 60,
            "hevcMain10", 10, "hdr10", "bt2020", "pq",
            "bt2020NonConstantLuminance", "limited", staticInfo, true,
            new EmptySampleProvider());

        staticInfo[0] = 99;
        assertEquals("hevcMain10", request.profile());
        assertEquals(10, request.bitDepth());
        assertEquals("hdr10", request.dynamicRange());
        assertEquals("bt2020", request.colorPrimaries());
        assertEquals("pq", request.transferFunction());
        assertEquals("bt2020NonConstantLuminance", request.matrixCoefficients());
        assertEquals("limited", request.colorRange());
        assertArrayEquals(sequence(25), request.hdrStaticInfo());
        assertTrue(request.hdrStaticInfoInBitstream());
        assertTrue(request.isHevcMain10Hdr10());
    }

    @Test
    public void legacyH264ConstructorRemainsSdrWithoutHdrFormatOverrides() {
        EncodedVideoDecodeRequest request = new EncodedVideoDecodeRequest(
            "h264", 1920, 1080, 60, new EmptySampleProvider());

        assertEquals("sdr", request.dynamicRange());
        assertEquals(8, request.bitDepth());
        assertEquals(0, request.hdrStaticInfo().length);
        assertFalse(request.isHevcMain10Hdr10());
    }

    private static byte[] sequence(int count) {
        byte[] result = new byte[count];
        for (int index = 0; index < count; index++) result[index] = (byte) index;
        return result;
    }

    private static final class EmptySampleProvider implements EncodedVideoSampleProvider {
        @Override public EncodedVideoSample nextSample() { return EncodedVideoSample.eos(); }
        @Override public int maxSampleBytes() { return 0; }
    }
}
