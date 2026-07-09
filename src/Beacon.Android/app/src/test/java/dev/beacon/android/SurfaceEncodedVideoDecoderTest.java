package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertSame;
import static org.junit.Assert.assertTrue;

public final class SurfaceEncodedVideoDecoderTest {
    @Test
    public void failsWhenSurfaceIsNotReady() {
        RecordingCodecFactory factory = new RecordingCodecFactory();
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(factory, new RecordingSurfaceProvider(null));

        EncodedVideoDecodeResult result = decoder.start(new EncodedVideoDecodeRequest(validPlan()));

        assertFalse(result.success());
        assertEquals("Encoded video surface is not ready.", result.diagnostic());
        assertEquals(0, factory.createCount);
    }

    @Test
    public void configuresAndStartsCodecWithPlanAndSurface() {
        Object surface = new Object();
        RecordingCodec codec = new RecordingCodec();
        RecordingCodecFactory factory = new RecordingCodecFactory(codec);
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(factory, new RecordingSurfaceProvider(surface));

        EncodedVideoDecodeResult result = decoder.start(new EncodedVideoDecodeRequest(validPlan()));

        assertTrue(result.success());
        assertEquals(
            "MediaCodec decoder configured. codec=h264 container=annex-b video=beacon-test://video/color-bars.h264 1280x720@60",
            result.status());
        assertEquals(1, factory.createCount);
        assertEquals("h264", factory.lastCodec);
        assertEquals("h264", codec.configuredPlan.codec());
        assertSame(surface, codec.configuredSurface);
        assertEquals(1, codec.configureCount);
        assertEquals(1, codec.startCount);
    }

    @Test
    public void stopStopsAndReleasesActiveCodecOnce() {
        RecordingCodec codec = new RecordingCodec();
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(
            new RecordingCodecFactory(codec),
            new RecordingSurfaceProvider(new Object()));
        decoder.start(new EncodedVideoDecodeRequest(validPlan()));

        decoder.stop();
        decoder.stop();

        assertEquals(1, codec.stopCount);
        assertEquals(1, codec.releaseCount);
    }

    @Test
    public void releasesCodecWhenConfigureFails() {
        RecordingCodec codec = new RecordingCodec();
        codec.configureFailure = new IllegalStateException("codec configure failed");
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(
            new RecordingCodecFactory(codec),
            new RecordingSurfaceProvider(new Object()));

        EncodedVideoDecodeResult result = decoder.start(new EncodedVideoDecodeRequest(validPlan()));

        assertFalse(result.success());
        assertEquals("codec configure failed", result.diagnostic());
        assertEquals(1, codec.releaseCount);
        assertEquals(0, codec.startCount);
    }

    @Test
    public void releasesCodecWhenStartFails() {
        RecordingCodec codec = new RecordingCodec();
        codec.startFailure = new IllegalStateException("codec start failed");
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(
            new RecordingCodecFactory(codec),
            new RecordingSurfaceProvider(new Object()));

        EncodedVideoDecodeResult result = decoder.start(new EncodedVideoDecodeRequest(validPlan()));

        assertFalse(result.success());
        assertEquals("codec start failed", result.diagnostic());
        assertEquals(1, codec.releaseCount);
        assertEquals(1, codec.configureCount);
    }

    private static EncodedVideoStreamPlan validPlan() {
        return EncodedVideoStreamPlan.from(EncodedVideoNativeStreamClientTest.validConnection());
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
        private RuntimeException configureFailure;
        private RuntimeException startFailure;
        private int configureCount;
        private int startCount;
        private int stopCount;
        private int releaseCount;

        @Override
        public void configure(EncodedVideoStreamPlan plan, Object surface) {
            configureCount++;
            if (configureFailure != null) {
                throw configureFailure;
            }

            configuredPlan = plan;
            configuredSurface = surface;
        }

        @Override
        public void start() {
            startCount++;
            if (startFailure != null) {
                throw startFailure;
            }
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
}
