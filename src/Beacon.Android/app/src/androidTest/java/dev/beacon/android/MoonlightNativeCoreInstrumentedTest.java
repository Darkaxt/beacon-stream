package dev.beacon.android;

import android.graphics.SurfaceTexture;
import android.view.Surface;

import junit.framework.TestCase;

import dev.beacon.streaming.moonlight.MoonlightNativeCore;
import dev.beacon.streaming.moonlight.MoonlightNativeConnection;
import dev.beacon.streaming.moonlight.MoonlightNativeSessionPlan;

public final class MoonlightNativeCoreInstrumentedTest extends TestCase {
    public void testNativeCoreLoadsAndReportsStableIdentity() {
        assertTrue(MoonlightNativeCore.isAvailable());
        assertEquals("moonlight-common-c", MoonlightNativeCore.identity());
        assertEquals("platform initialization", MoonlightNativeCore.stageName(1));
        assertEquals("LiStartConnection", MoonlightNativeConnection.bindingIdentity());
    }

    public void testOwningClientResponseMapsIntoNativePlan() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{" +
                "\"stream\":{\"connection\":{\"protocol\":\"gamestream\"}}," +
                "\"nativeSession\":{" +
                "\"address\":\"10.0.2.2\"," +
                "\"serverAppVersion\":\"7.1.431.0\"," +
                "\"serverGfeVersion\":null," +
                "\"rtspSessionUrl\":\"rtsp://10.0.2.2:48010/session/123\"," +
                "\"serverCodecModeSupport\":769," +
                "\"width\":2560," +
                "\"height\":1600," +
                "\"fps\":120," +
                "\"bitrateKbps\":45000," +
                "\"packetSize\":1024," +
                "\"streamingMode\":\"local\"," +
                "\"audioConfiguration\":\"stereo\"," +
                "\"videoFormat\":\"hevc-main10\"," +
                "\"clientRefreshRateX100\":12000," +
                "\"colorSpace\":\"rec2020\"," +
                "\"colorRange\":\"full\"," +
                "\"encryptionMode\":\"all\"," +
                "\"remoteInputAesKey\":\"AAECAwQFBgcICQoLDA0ODw==\"," +
                "\"remoteInputAesIv\":\"EBESExQVFhcYGRobHB0eHw==\"}}"
        );

        assertTrue(descriptor.nativeSessionValid());
        MoonlightNativeSessionPlan plan = descriptor.nativeSession();
        assertNotNull(plan);
        assertEquals(2560, plan.width());
        assertEquals(1600, plan.height());
        assertEquals(120, plan.fps());
        assertEquals(MoonlightNativeSessionPlan.VIDEO_FORMAT_H265_MAIN10, plan.supportedVideoFormats());
        assertEquals(MoonlightNativeSessionPlan.ENCFLG_ALL, plan.encryptionFlags());
    }

    public void testNativeVideoRendererConfiguresRealMediaCodecSurface() {
        SurfaceTexture texture = new SurfaceTexture(0);
        Surface surface = new Surface(texture);
        MoonlightMediaCodecVideoRenderer renderer = new MoonlightMediaCodecVideoRenderer(
            new AndroidMediaCodecFactory(),
            () -> surface);
        try {
            int result = renderer.setup(
                MoonlightNativeSessionPlan.VIDEO_FORMAT_H264,
                1280,
                720,
                60);

            assertEquals(renderer.diagnostic(), 0, result);
        } finally {
            renderer.cleanup();
            surface.release();
            texture.release();
        }
    }
}
