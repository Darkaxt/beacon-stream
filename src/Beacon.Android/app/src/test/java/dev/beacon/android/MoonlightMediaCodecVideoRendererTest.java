package dev.beacon.android;

import dev.beacon.streaming.moonlight.MoonlightNativeSessionPlan;
import dev.beacon.streaming.moonlight.MoonlightVideoRenderer;

import org.junit.Test;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertSame;
import static org.junit.Assert.assertTrue;

public final class MoonlightMediaCodecVideoRendererTest {
    @Test
    public void boundedQueueDeliversSamplesAndDeterministicEndOfStream() {
        MoonlightVideoSampleProvider samples = new MoonlightVideoSampleProvider();

        samples.submit(new byte[] {1, 2, 3}, 4444);
        EncodedVideoSample sample = samples.nextSample();
        samples.finish();
        EncodedVideoSample end = samples.nextSample();

        assertArrayEquals(new byte[] {1, 2, 3}, sample.data());
        assertEquals(4444, sample.presentationTimeUs());
        assertFalse(sample.endOfStream());
        assertTrue(end.endOfStream());
    }

    @Test
    public void finishReleasesProducerBlockedByBoundedCapacity() throws Exception {
        MoonlightVideoSampleProvider samples = new MoonlightVideoSampleProvider(1);
        samples.submit(new byte[] {1}, 1);
        CountDownLatch producerStarted = new CountDownLatch(1);
        AtomicReference<Throwable> producerFailure = new AtomicReference<>();
        Thread producer = new Thread(() -> {
            producerStarted.countDown();
            try {
                samples.submit(new byte[] {2}, 2);
            } catch (Throwable ex) {
                producerFailure.set(ex);
            }
        });

        producer.start();
        producerStarted.await();
        samples.finish();
        producer.join();

        assertEquals(null, producerFailure.get());
        assertTrue(samples.nextSample().endOfStream());
    }

    @Test
    public void configuresMediaCodecAndQueuesNativeDecodeUnits() {
        RecordingCodec codec = new RecordingCodec();
        Object surface = new Object();
        MoonlightMediaCodecVideoRenderer renderer = new MoonlightMediaCodecVideoRenderer(
            requestedCodec -> {
                codec.requestedCodec = requestedCodec;
                return codec;
            },
            () -> surface);

        int setup = renderer.setup(
            MoonlightNativeSessionPlan.VIDEO_FORMAT_H265_MAIN10,
            2560,
            1600,
            120);
        int submit = renderer.submitDecodeUnit(
            new byte[] {9, 8, 7, 6, 5},
            3,
            MoonlightVideoRenderer.BUFFER_TYPE_PICDATA,
            1,
            77,
            123456);

        assertEquals(0, setup);
        assertEquals(MoonlightVideoRenderer.DR_OK, submit);
        assertEquals("hevc", codec.requestedCodec);
        assertSame(surface, codec.surface);
        assertEquals(2560, codec.plan.width());
        assertEquals(1600, codec.plan.height());
        assertEquals(120, codec.plan.fps());
        assertEquals("annex-b", codec.plan.container());
        assertTrue(codec.started);
        EncodedVideoSample queued = codec.samples.nextSample();
        assertArrayEquals(new byte[] {9, 8, 7}, queued.data());
        assertEquals(123456, queued.presentationTimeUs());

        renderer.stop();
        assertTrue(codec.samples.nextSample().endOfStream());
        renderer.cleanup();
        assertTrue(codec.stopped);
        assertTrue(codec.released);
    }

    @Test
    public void mapsEveryNativeCodecFamilyWithoutChangingDimensions() {
        assertCodec(MoonlightNativeSessionPlan.VIDEO_FORMAT_H264, "h264");
        assertCodec(MoonlightNativeSessionPlan.VIDEO_FORMAT_H264_HIGH8_444, "h264");
        assertCodec(MoonlightNativeSessionPlan.VIDEO_FORMAT_H265, "hevc");
        assertCodec(MoonlightNativeSessionPlan.VIDEO_FORMAT_H265_REXT10_444, "hevc");
        assertCodec(MoonlightNativeSessionPlan.VIDEO_FORMAT_AV1_MAIN8, "av1");
        assertCodec(MoonlightNativeSessionPlan.VIDEO_FORMAT_AV1_HIGH10_444, "av1");
    }

    @Test
    public void setupFailureIsReturnedToNativeCoreAndReleasesCodec() {
        RecordingCodec codec = new RecordingCodec();
        codec.configureFailure = new IllegalStateException("codec rejected surface");
        MoonlightMediaCodecVideoRenderer renderer = new MoonlightMediaCodecVideoRenderer(
            ignored -> codec,
            Object::new);

        int result = renderer.setup(MoonlightNativeSessionPlan.VIDEO_FORMAT_H264, 1920, 1080, 60);

        assertEquals(-1, result);
        assertTrue(renderer.diagnostic().contains("codec rejected surface"));
        assertTrue(codec.released);
    }

    private static void assertCodec(int videoFormat, String expectedCodec) {
        RecordingCodec codec = new RecordingCodec();
        MoonlightMediaCodecVideoRenderer renderer = new MoonlightMediaCodecVideoRenderer(
            requestedCodec -> {
                codec.requestedCodec = requestedCodec;
                return codec;
            },
            Object::new);

        assertEquals(0, renderer.setup(videoFormat, 1280, 800, 90));
        assertEquals(expectedCodec, codec.requestedCodec);
        assertEquals(1280, codec.plan.width());
        assertEquals(800, codec.plan.height());
        assertEquals(90, codec.plan.fps());
        renderer.cleanup();
    }

    private static final class RecordingCodec implements EncodedVideoCodec {
        private String requestedCodec;
        private EncodedVideoStreamPlan plan;
        private Object surface;
        private EncodedVideoSampleProvider samples;
        private RuntimeException configureFailure;
        private boolean started;
        private boolean stopped;
        private boolean released;

        @Override
        public void configure(
            EncodedVideoStreamPlan plan,
            Object surface,
            EncodedVideoSampleProvider sampleProvider) {
            if (configureFailure != null) {
                throw configureFailure;
            }
            this.plan = plan;
            this.surface = surface;
            this.samples = sampleProvider;
        }

        @Override
        public void start() {
            started = true;
        }

        @Override
        public void stop() {
            stopped = true;
        }

        @Override
        public void release() {
            released = true;
        }
    }
}
