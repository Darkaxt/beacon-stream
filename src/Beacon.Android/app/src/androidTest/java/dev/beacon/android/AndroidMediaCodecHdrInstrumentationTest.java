package dev.beacon.android;

import android.media.MediaCodecInfo;
import android.media.MediaFormat;

import androidx.test.ext.junit.runners.AndroidJUnit4;

import org.junit.Test;
import org.junit.runner.RunWith;

import java.nio.ByteBuffer;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;

@RunWith(AndroidJUnit4.class)
public final class AndroidMediaCodecHdrInstrumentationTest {
    @Test
    public void hdrRequestBuildsExactMain10Hdr10MediaFormat() {
        EncodedVideoDecodeRequest request = hdrRequest();

        MediaFormat format = AndroidMediaCodecFactory.buildMediaFormat(request, 4096, false);

        assertEquals(MediaFormat.MIMETYPE_VIDEO_HEVC, format.getString(MediaFormat.KEY_MIME));
        assertEquals(
            MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10,
            format.getInteger(MediaFormat.KEY_PROFILE));
        assertEquals(MediaFormat.COLOR_STANDARD_BT2020, format.getInteger(MediaFormat.KEY_COLOR_STANDARD));
        assertEquals(MediaFormat.COLOR_TRANSFER_ST2084, format.getInteger(MediaFormat.KEY_COLOR_TRANSFER));
        assertEquals(MediaFormat.COLOR_RANGE_LIMITED, format.getInteger(MediaFormat.KEY_COLOR_RANGE));
        assertArrayEquals(sequence(25), bytes(format.getByteBuffer(MediaFormat.KEY_HDR_STATIC_INFO)));
    }

    @Test
    public void h264SdrFormatKeepsPlatformColorDefaults() {
        EncodedVideoDecodeRequest request = new EncodedVideoDecodeRequest(
            "h264", 1920, 1080, 60, new EmptySampleProvider());

        MediaFormat format = AndroidMediaCodecFactory.buildMediaFormat(request, 0, false);

        assertEquals(MediaFormat.MIMETYPE_VIDEO_AVC, format.getString(MediaFormat.KEY_MIME));
        assertFalse(format.containsKey(MediaFormat.KEY_PROFILE));
        assertFalse(format.containsKey(MediaFormat.KEY_COLOR_STANDARD));
        assertFalse(format.containsKey(MediaFormat.KEY_COLOR_TRANSFER));
        assertFalse(format.containsKey(MediaFormat.KEY_COLOR_RANGE));
        assertNull(format.getByteBuffer(MediaFormat.KEY_HDR_STATIC_INFO));
    }

    private static EncodedVideoDecodeRequest hdrRequest() {
        return new EncodedVideoDecodeRequest(
            "hevc", 3840, 2160, 60,
            "hevcMain10", 10, "hdr10", "bt2020", "pq",
            "bt2020NonConstantLuminance", "limited", sequence(25), true,
            new EmptySampleProvider());
    }

    private static byte[] bytes(ByteBuffer source) {
        ByteBuffer copy = source.asReadOnlyBuffer();
        byte[] result = new byte[copy.remaining()];
        copy.get(result);
        return result;
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
