package dev.beacon.android;

import android.media.MediaCodecInfo;

import org.junit.Test;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class AndroidMediaCodecCatalogTest {
    @Test
    public void genericHevcMain10IsNotReportedAsHdr10() {
        assertFalse(AndroidMediaCodecCatalog.isHevcHdr10Profile(
            "video/hevc", MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10));
        assertTrue(AndroidMediaCodecCatalog.isHevcHdr10Profile(
            "video/hevc", MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10));
        assertTrue(AndroidMediaCodecCatalog.isHevcHdr10Profile(
            "video/hevc", MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10Plus));
    }
}
