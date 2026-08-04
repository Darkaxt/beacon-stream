package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayDeque;
import java.util.Arrays;
import java.util.Queue;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertSame;
import static org.junit.Assert.assertTrue;

public final class SurfaceEncodedVideoDecoderTest {
    @Test
    public void failsWhenSurfaceIsNotReady() {
        RecordingCodecFactory factory = new RecordingCodecFactory();
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(factory, new RecordingSurfaceProvider(null));

        EncodedVideoDecodeResult result = decoder.start(validRequest());

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

        EncodedVideoDecodeResult result = decoder.start(validRequest());

        assertTrue(result.success());
        assertEquals(
            "MediaCodec decoder configured. codec=h264 1280x720@60",
            result.status());
        assertEquals(1, factory.createCount);
        assertEquals("h264", factory.lastCodec);
        assertEquals("h264", codec.configuredRequest.codec());
        assertSame(surface, codec.configuredSurface);
        assertEquals(1, codec.configureCount);
        assertEquals(1, codec.startCount);
    }

    @Test
    public void configuresCodecWithSampleProviderCreatedForPlan() {
        Object surface = new Object();
        RecordingCodec codec = new RecordingCodec();
        RecordingSampleProvider sampleProvider = new RecordingSampleProvider();
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(
            new RecordingCodecFactory(codec),
            new RecordingSurfaceProvider(surface));

        EncodedVideoDecodeResult result = decoder.start(validRequest(sampleProvider));

        assertTrue(result.success());
        assertSame(sampleProvider, codec.configuredSampleProvider);
    }

    @Test
    public void configuresCodecWithAsynchronousObserverBeforeStart() {
        RecordingCodec codec = new RecordingCodec();
        RecordingCodecObserver observer = new RecordingCodecObserver();
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(
            new RecordingCodecFactory(codec),
            new RecordingSurfaceProvider(new Object()),
            observer);

        EncodedVideoDecodeResult result = decoder.start(validRequest());

        assertTrue(result.success());
        assertTrue(observer != codec.configuredObserver);
        codec.configuredObserver.onEndOfStream();
        assertEquals(1, observer.endOfStreamCount);
        assertEquals(Arrays.asList("configure", "start"), codec.events);
        decoder.stop();
    }

    @Test
    public void reconnectStopsAndReleasesPreviousCodecBeforeReplacement() {
        RecordingCodec first = new RecordingCodec();
        RecordingCodec second = new RecordingCodec();
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(
            new SequenceCodecFactory(first, second),
            new RecordingSurfaceProvider(new Object()),
            EncodedVideoCodecObserver.noOp());

        assertTrue(decoder.start(validRequest()).success());
        assertTrue(decoder.start(validRequest()).success());

        assertEquals(1, first.stopCount);
        assertEquals(1, first.releaseCount);
        assertEquals(1, second.startCount);
        decoder.stop();
        decoder.stop();
        assertEquals(1, second.stopCount);
        assertEquals(1, second.releaseCount);
    }

    @Test
    public void stopStopsAndReleasesActiveCodecOnce() {
        RecordingCodec codec = new RecordingCodec();
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(
            new RecordingCodecFactory(codec),
            new RecordingSurfaceProvider(new Object()));
        decoder.start(validRequest());

        decoder.stop();
        decoder.stop();

        assertEquals(1, codec.stopCount);
        assertEquals(1, codec.releaseCount);
    }

    @Test
    public void callbacksFromAReplacedCodecCannotReachTheActiveSession() {
        RecordingCodec first = new RecordingCodec();
        RecordingCodec second = new RecordingCodec();
        RecordingCodecObserver observer = new RecordingCodecObserver();
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(
            new SequenceCodecFactory(first, second),
            new RecordingSurfaceProvider(new Object()),
            observer);
        decoder.start(validRequest());
        EncodedVideoCodecObserver staleObserver = first.configuredObserver;

        decoder.start(validRequest());
        staleObserver.onError(new IllegalStateException("stale"));

        assertEquals(0, observer.errorCount);
        decoder.stop();
    }

    @Test
    public void releasesCodecWhenConfigureFails() {
        RecordingCodec codec = new RecordingCodec();
        codec.configureFailure = new IllegalStateException("codec configure failed");
        SurfaceEncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(
            new RecordingCodecFactory(codec),
            new RecordingSurfaceProvider(new Object()));

        EncodedVideoDecodeResult result = decoder.start(validRequest());

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

        EncodedVideoDecodeResult result = decoder.start(validRequest());

        assertFalse(result.success());
        assertEquals("codec start failed", result.diagnostic());
        assertEquals(1, codec.releaseCount);
        assertEquals(1, codec.configureCount);
    }

    private static EncodedVideoDecodeRequest validRequest() {
        return validRequest(EncodedVideoSampleProvider.endOfStreamOnly());
    }

    private static EncodedVideoDecodeRequest validRequest(EncodedVideoSampleProvider sampleProvider) {
        return new EncodedVideoDecodeRequest("h264", 1280, 720, 60, sampleProvider);
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

    private static final class SequenceCodecFactory implements EncodedVideoCodecFactory {
        private final Queue<RecordingCodec> codecs = new ArrayDeque<>();

        SequenceCodecFactory(RecordingCodec... codecs) {
            this.codecs.addAll(Arrays.asList(codecs));
        }

        @Override
        public EncodedVideoCodec create(String codec) {
            return codecs.remove();
        }
    }

    private static final class RecordingCodec implements EncodedVideoCodec {
        private EncodedVideoDecodeRequest configuredRequest;
        private Object configuredSurface;
        private EncodedVideoSampleProvider configuredSampleProvider;
        private EncodedVideoCodecObserver configuredObserver;
        private RuntimeException configureFailure;
        private RuntimeException startFailure;
        private final java.util.List<String> events = new java.util.ArrayList<>();
        private int configureCount;
        private int startCount;
        private int stopCount;
        private int releaseCount;

        @Override
        public void configure(EncodedVideoDecodeRequest request, Object surface, EncodedVideoSampleProvider sampleProvider) {
            configure(request, surface, sampleProvider, EncodedVideoCodecObserver.noOp());
        }

        @Override
        public void configure(
            EncodedVideoDecodeRequest request,
            Object surface,
            EncodedVideoSampleProvider sampleProvider,
            EncodedVideoCodecObserver observer) {
            configureCount++;
            events.add("configure");
            if (configureFailure != null) {
                throw configureFailure;
            }

            configuredRequest = request;
            configuredSurface = surface;
            configuredSampleProvider = sampleProvider;
            configuredObserver = observer;
        }

        @Override
        public void start() {
            startCount++;
            events.add("start");
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

    private static final class RecordingSampleProvider implements EncodedVideoSampleProvider {
        @Override
        public EncodedVideoSample nextSample() {
            return EncodedVideoSample.eos();
        }
    }

    private static final class RecordingCodecObserver implements EncodedVideoCodecObserver {
        private int errorCount;
        private int endOfStreamCount;
        @Override public void onInputQueued(
            long frameSequence,
            long presentationTimeUs,
            long queuedAtNs) { }
        @Override public void onOutputReleased(
            long frameSequence,
            long presentationTimeUs,
            long releasedAtNs,
            boolean rendered) { }
        @Override public void onFrameRendered(
            long frameSequence,
            long presentationTimeUs,
            long renderedAtNs) { }
        @Override public void onEndOfStream() { endOfStreamCount++; }
        @Override public void onError(Throwable failure) { errorCount++; }
    }
}
