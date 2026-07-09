package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertSame;
import static org.junit.Assert.assertTrue;

public final class GameStreamRtpDecoderVideoConsumerTest {
    @Test
    public void startsDecoderWithRtpSampleProvider() {
        Object surface = new Object();
        RecordingCodec codec = new RecordingCodec();
        RecordingCodecFactory codecFactory = new RecordingCodecFactory(codec);
        RecordingSampleProvider sampleProvider = new RecordingSampleProvider();
        GameStreamRtpDecoderVideoConsumer consumer = new GameStreamRtpDecoderVideoConsumer(
            codecFactory,
            new RecordingSurfaceProvider(surface));

        NativeStreamStartResult result = consumer.start(completePlan(), sessionInfo(), sampleProvider);

        assertTrue(result.success());
        assertEquals(
            "MediaCodec decoder configured. codec=h264 container=annex-b video=udp://127.0.0.1:47998 2560x1600@120",
            result.status());
        assertTrue(result.presentation().active());
        assertEquals("encoded-video", result.presentation().kind());
        assertEquals("udp://127.0.0.1:47998", result.presentation().endpointUri());
        assertEquals("Beacon encoded video h264 2560x1600@120", result.presentation().label());
        assertEquals(1, codecFactory.createCount);
        assertEquals("h264", codecFactory.lastCodec);
        assertEquals(1, codec.configureCount);
        assertEquals(1, codec.startCount);
        assertEquals("udp://127.0.0.1:47998", codec.configuredPlan.videoUri());
        assertEquals(2560, codec.configuredPlan.width());
        assertEquals(1600, codec.configuredPlan.height());
        assertEquals(120, codec.configuredPlan.fps());
        assertSame(surface, codec.configuredSurface);
        assertSame(sampleProvider, codec.configuredSampleProvider);
    }

    @Test
    public void incompleteMetadataReturnsDiagnostic() {
        GameStreamRtpDecoderVideoConsumer consumer = new GameStreamRtpDecoderVideoConsumer(
            new RecordingCodecFactory(),
            new RecordingSurfaceProvider(new Object()));

        NativeStreamStartResult result = consumer.start(planWithoutMetadata(), sessionInfo(), new RecordingSampleProvider());

        assertFalse(result.success());
        assertEquals(
            "GameStream RTP video metadata is incomplete. Missing or invalid: codec, container, width, height, fps.",
            result.diagnostic());
    }

    @Test
    public void stopReleasesActiveDecoderOnce() {
        RecordingCodec codec = new RecordingCodec();
        GameStreamRtpDecoderVideoConsumer consumer = new GameStreamRtpDecoderVideoConsumer(
            new RecordingCodecFactory(codec),
            new RecordingSurfaceProvider(new Object()));

        NativeStreamStartResult result = consumer.start(completePlan(), sessionInfo(), new RecordingSampleProvider());
        consumer.stop();
        consumer.stop();

        assertTrue(result.success());
        assertEquals(1, codec.stopCount);
        assertEquals(1, codec.releaseCount);
    }

    private static GameStreamEndpointPlan completePlan() {
        return GameStreamEndpointPlan.from(connection(
            "\"codec\":\"h264\",\"container\":\"annex-b\",\"width\":\"2560\",\"height\":\"1600\",\"fps\":\"120\""));
    }

    private static GameStreamEndpointPlan planWithoutMetadata() {
        return GameStreamEndpointPlan.from(connection(""));
    }

    private static StreamConnectionDescriptor connection(String metadata) {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010/beacon/session\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}],\"metadata\":{" +
                metadata +
                "}}}}");
    }

    private static GameStreamRtspSessionInfo sessionInfo() {
        return GameStreamRtspSessionInfo.startedWithClientPorts(
            "gamestream",
            "rtsp://127.0.0.1:48010/beacon/session",
            "session-1",
            50000,
            48000,
            50002,
            47998,
            50004,
            47999);
    }

    private static final class RecordingSurfaceProvider implements EncodedVideoSurfaceProvider {
        private final Object surface;

        RecordingSurfaceProvider(Object surface) {
            this.surface = surface;
        }

        @Override
        public Object currentSurface() {
            return surface;
        }
    }

    private static final class RecordingCodecFactory implements EncodedVideoCodecFactory {
        private final RecordingCodec codec;
        private int createCount;
        private String lastCodec = "";

        RecordingCodecFactory() {
            this(new RecordingCodec());
        }

        RecordingCodecFactory(RecordingCodec codec) {
            this.codec = codec;
        }

        @Override
        public EncodedVideoCodec create(String codec) {
            createCount++;
            lastCodec = codec;
            return this.codec;
        }
    }

    private static final class RecordingCodec implements EncodedVideoCodec {
        private EncodedVideoStreamPlan configuredPlan;
        private Object configuredSurface;
        private EncodedVideoSampleProvider configuredSampleProvider;
        private int configureCount;
        private int startCount;
        private int stopCount;
        private int releaseCount;

        @Override
        public void configure(EncodedVideoStreamPlan plan, Object surface, EncodedVideoSampleProvider sampleProvider) {
            configureCount++;
            configuredPlan = plan;
            configuredSurface = surface;
            configuredSampleProvider = sampleProvider;
        }

        @Override
        public void start() {
            startCount++;
        }

        @Override
        public void stop() {
            stopCount++;
        }

        @Override
        public void release() {
            releaseCount++;
        }
    }

    private static final class RecordingSampleProvider implements EncodedVideoSampleProvider {
        @Override
        public EncodedVideoSample nextSample() {
            return EncodedVideoSample.eos();
        }
    }
}
