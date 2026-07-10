package dev.beacon.streaming.moonlight;

import org.junit.Test;

import java.util.Base64;
import java.util.LinkedHashMap;
import java.util.Map;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

public final class MoonlightNativeSessionPlanTest {
    @Test
    public void mapsCompleteDescriptorWithoutChangingServerPolicy() {
        TestDescriptor descriptor = new TestDescriptor();

        MoonlightNativeSessionPlan plan = descriptor.build();

        assertEquals(descriptor.address, plan.address());
        assertEquals(descriptor.serverAppVersion, plan.serverAppVersion());
        assertEquals(descriptor.serverGfeVersion, plan.serverGfeVersion());
        assertEquals(descriptor.rtspSessionUrl, plan.rtspSessionUrl());
        assertEquals(descriptor.serverCodecModeSupport, plan.serverCodecModeSupport());
        assertEquals(descriptor.width, plan.width());
        assertEquals(descriptor.height, plan.height());
        assertEquals(descriptor.fps, plan.fps());
        assertEquals(descriptor.bitrateKbps, plan.bitrateKbps());
        assertEquals(descriptor.packetSize, plan.packetSize());
        assertEquals(MoonlightNativeSessionPlan.STREAM_CFG_LOCAL, plan.streamingRemotely());
        assertEquals(MoonlightNativeSessionPlan.AUDIO_CONFIGURATION_STEREO, plan.audioConfiguration());
        assertEquals(MoonlightNativeSessionPlan.VIDEO_FORMAT_H265_MAIN10, plan.supportedVideoFormats());
        assertEquals(descriptor.clientRefreshRateX100, plan.clientRefreshRateX100());
        assertEquals(MoonlightNativeSessionPlan.COLORSPACE_REC_2020, plan.colorSpace());
        assertEquals(MoonlightNativeSessionPlan.COLOR_RANGE_FULL, plan.colorRange());
        assertEquals(MoonlightNativeSessionPlan.ENCFLG_ALL, plan.encryptionFlags());
        assertArrayEquals(descriptor.keyBytes, plan.remoteInputAesKey());
        assertArrayEquals(descriptor.ivBytes, plan.remoteInputAesIv());

        byte[] returnedKey = plan.remoteInputAesKey();
        returnedKey[0] = 99;
        assertArrayEquals(descriptor.keyBytes, plan.remoteInputAesKey());
        assertTrue(plan.toString().contains("[redacted]"));
        assertTrue(!plan.toString().contains(descriptor.remoteInputAesKey));
        assertTrue(!plan.toString().contains(descriptor.remoteInputAesIv));
    }

    @Test
    public void mapsEveryNamedMoonlightValueExactly() {
        assertMappings(
            Map.of(
                "local", MoonlightNativeSessionPlan.STREAM_CFG_LOCAL,
                "remote", MoonlightNativeSessionPlan.STREAM_CFG_REMOTE,
                "auto", MoonlightNativeSessionPlan.STREAM_CFG_AUTO),
            (descriptor, value) -> descriptor.streamingMode = value,
            plan -> plan.streamingRemotely());

        assertMappings(
            Map.of(
                "stereo", MoonlightNativeSessionPlan.AUDIO_CONFIGURATION_STEREO,
                "5.1", MoonlightNativeSessionPlan.AUDIO_CONFIGURATION_51_SURROUND,
                "7.1", MoonlightNativeSessionPlan.AUDIO_CONFIGURATION_71_SURROUND),
            (descriptor, value) -> descriptor.audioConfiguration = value,
            plan -> plan.audioConfiguration());

        Map<String, Integer> videoFormats = new LinkedHashMap<>();
        videoFormats.put("h264", MoonlightNativeSessionPlan.VIDEO_FORMAT_H264);
        videoFormats.put("h264-high8-444", MoonlightNativeSessionPlan.VIDEO_FORMAT_H264_HIGH8_444);
        videoFormats.put("hevc", MoonlightNativeSessionPlan.VIDEO_FORMAT_H265);
        videoFormats.put("hevc-main10", MoonlightNativeSessionPlan.VIDEO_FORMAT_H265_MAIN10);
        videoFormats.put("hevc-rext8-444", MoonlightNativeSessionPlan.VIDEO_FORMAT_H265_REXT8_444);
        videoFormats.put("hevc-rext10-444", MoonlightNativeSessionPlan.VIDEO_FORMAT_H265_REXT10_444);
        videoFormats.put("av1-main8", MoonlightNativeSessionPlan.VIDEO_FORMAT_AV1_MAIN8);
        videoFormats.put("av1-main10", MoonlightNativeSessionPlan.VIDEO_FORMAT_AV1_MAIN10);
        videoFormats.put("av1-high8-444", MoonlightNativeSessionPlan.VIDEO_FORMAT_AV1_HIGH8_444);
        videoFormats.put("av1-high10-444", MoonlightNativeSessionPlan.VIDEO_FORMAT_AV1_HIGH10_444);
        assertMappings(
            videoFormats,
            (descriptor, value) -> descriptor.videoFormat = value,
            plan -> plan.supportedVideoFormats());

        assertMappings(
            Map.of(
                "rec601", MoonlightNativeSessionPlan.COLORSPACE_REC_601,
                "rec709", MoonlightNativeSessionPlan.COLORSPACE_REC_709,
                "rec2020", MoonlightNativeSessionPlan.COLORSPACE_REC_2020),
            (descriptor, value) -> descriptor.colorSpace = value,
            plan -> plan.colorSpace());

        assertMappings(
            Map.of(
                "limited", MoonlightNativeSessionPlan.COLOR_RANGE_LIMITED,
                "full", MoonlightNativeSessionPlan.COLOR_RANGE_FULL),
            (descriptor, value) -> descriptor.colorRange = value,
            plan -> plan.colorRange());

        assertMappings(
            Map.of(
                "none", MoonlightNativeSessionPlan.ENCFLG_NONE,
                "audio", MoonlightNativeSessionPlan.ENCFLG_AUDIO,
                "video", MoonlightNativeSessionPlan.ENCFLG_VIDEO,
                "all", MoonlightNativeSessionPlan.ENCFLG_ALL),
            (descriptor, value) -> descriptor.encryptionMode = value,
            plan -> plan.encryptionFlags());
    }

    @Test
    public void rejectsMalformedOrUnsupportedDescriptorFields() {
        TestDescriptor malformedBase64 = new TestDescriptor();
        malformedBase64.remoteInputAesKey = "not-base64";
        assertRejected(malformedBase64, "remoteInputAesKey");

        TestDescriptor wrongKeyLength = new TestDescriptor();
        wrongKeyLength.remoteInputAesKey = Base64.getEncoder().encodeToString(new byte[15]);
        assertRejected(wrongKeyLength, "remoteInputAesKey");

        TestDescriptor wrongIvLength = new TestDescriptor();
        wrongIvLength.remoteInputAesIv = Base64.getEncoder().encodeToString(new byte[17]);
        assertRejected(wrongIvLength, "remoteInputAesIv");

        TestDescriptor unsupportedVideo = new TestDescriptor();
        unsupportedVideo.videoFormat = "vp9";
        assertRejected(unsupportedVideo, "videoFormat");

        TestDescriptor missingAddress = new TestDescriptor();
        missingAddress.address = " ";
        assertRejected(missingAddress, "address");

        TestDescriptor invalidRtsp = new TestDescriptor();
        invalidRtsp.rtspSessionUrl = "https://10.0.2.2/session/123";
        assertRejected(invalidRtsp, "rtspSessionUrl");
    }

    private static void assertRejected(TestDescriptor descriptor, String expectedField) {
        IllegalArgumentException error = assertThrows(IllegalArgumentException.class, descriptor::build);
        assertTrue(error.getMessage().contains(expectedField));
    }

    private static void assertMappings(
        Map<String, Integer> expected,
        DescriptorValueSetter setter,
        PlanValueGetter getter) {
        for (Map.Entry<String, Integer> entry : expected.entrySet()) {
            TestDescriptor descriptor = new TestDescriptor();
            setter.set(descriptor, entry.getKey());
            assertEquals(entry.getValue().intValue(), getter.get(descriptor.build()));
        }
    }

    private interface DescriptorValueSetter {
        void set(TestDescriptor descriptor, String value);
    }

    private interface PlanValueGetter {
        int get(MoonlightNativeSessionPlan plan);
    }

    private static final class TestDescriptor {
        private String address = "10.0.2.2";
        private String serverAppVersion = "7.1.431.0";
        private String serverGfeVersion = "3.27.0.120";
        private String rtspSessionUrl = "rtsp://10.0.2.2:48010/session/123";
        private int serverCodecModeSupport = 0x0301;
        private int width = 2560;
        private int height = 1600;
        private int fps = 120;
        private int bitrateKbps = 45000;
        private int packetSize = 1024;
        private String streamingMode = "local";
        private String audioConfiguration = "stereo";
        private String videoFormat = "hevc-main10";
        private int clientRefreshRateX100 = 12000;
        private String colorSpace = "rec2020";
        private String colorRange = "full";
        private String encryptionMode = "all";
        private final byte[] keyBytes = sequence(0);
        private final byte[] ivBytes = sequence(16);
        private String remoteInputAesKey = Base64.getEncoder().encodeToString(keyBytes);
        private String remoteInputAesIv = Base64.getEncoder().encodeToString(ivBytes);

        private MoonlightNativeSessionPlan build() {
            return MoonlightNativeSessionPlan.create(
                address,
                serverAppVersion,
                serverGfeVersion,
                rtspSessionUrl,
                serverCodecModeSupport,
                width,
                height,
                fps,
                bitrateKbps,
                packetSize,
                streamingMode,
                audioConfiguration,
                videoFormat,
                clientRefreshRateX100,
                colorSpace,
                colorRange,
                encryptionMode,
                remoteInputAesKey,
                remoteInputAesIv);
        }

        private static byte[] sequence(int start) {
            byte[] bytes = new byte[16];
            for (int index = 0; index < bytes.length; index++) {
                bytes[index] = (byte) (start + index);
            }
            return bytes;
        }
    }
}
