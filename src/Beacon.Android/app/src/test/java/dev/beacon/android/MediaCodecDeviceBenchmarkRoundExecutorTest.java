package dev.beacon.android;

import com.google.gson.JsonParser;

import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

public final class MediaCodecDeviceBenchmarkRoundExecutorTest {
    @Test
    public void reportsDecodeAndPresentedFrameMeasurementsFromCodecCallbacks() {
        RecordingCodec codec = new RecordingCodec();
        RecordingSurfaceFactory surfaces = new RecordingSurfaceFactory();
        MediaCodecDeviceBenchmarkRoundExecutor executor =
            new MediaCodecDeviceBenchmarkRoundExecutor(
                ignored -> codec,
                ignored -> twoFrameVector(),
                surfaces,
                Runnable::run);
        RecordingRoundObserver observer = new RecordingRoundObserver();

        executor.start(round(), observer);
        EncodedVideoSample first = codec.sampleProvider.nextSample();
        EncodedVideoSample second = codec.sampleProvider.nextSample();
        codec.observer.onInputQueued(first.presentationTimeUs(), 1_000_000);
        codec.observer.onInputQueued(second.presentationTimeUs(), 11_000_000);
        codec.observer.onOutputReleased(first.presentationTimeUs(), 6_000_000, true);
        surfaces.observer.onFramePresented(first.presentationTimeUs(), 10_000_000);
        codec.observer.onOutputReleased(second.presentationTimeUs(), 16_000_000, true);
        surfaces.presentOnClose(second.presentationTimeUs(), 20_000_000);
        codec.observer.onEndOfStream();

        String json = observer.sample.toJson().toString();
        assertTrue(json.contains("\"configured\":true"));
        assertTrue(json.contains("\"sustainedFps\":133.33333333333334"));
        assertTrue(json.contains("\"p95DecodeLatencyMs\":5.0"));
        assertTrue(json.contains("\"p95PresentationLatencyMs\":9.0"));
        assertTrue(json.contains("\"droppedFrames\":0"));
        assertTrue(codec.stopped);
        assertTrue(codec.released);
        assertTrue(surfaces.closed);
    }

    @Test
    public void endOfStreamCompletesAndCountsUndeliveredPresentationFramesAsDrops() {
        RecordingCodec codec = new RecordingCodec();
        RecordingSurfaceFactory surfaces = new RecordingSurfaceFactory();
        MediaCodecDeviceBenchmarkRoundExecutor executor =
            new MediaCodecDeviceBenchmarkRoundExecutor(
                ignored -> codec,
                ignored -> twoFrameVector(),
                surfaces,
                Runnable::run);
        RecordingRoundObserver observer = new RecordingRoundObserver();

        executor.start(round(), observer);
        EncodedVideoSample first = codec.sampleProvider.nextSample();
        EncodedVideoSample second = codec.sampleProvider.nextSample();
        codec.observer.onInputQueued(first.presentationTimeUs(), 1_000_000);
        codec.observer.onInputQueued(second.presentationTimeUs(), 11_000_000);
        codec.observer.onOutputReleased(first.presentationTimeUs(), 6_000_000, true);
        surfaces.observer.onFramePresented(first.presentationTimeUs(), 10_000_000);
        codec.observer.onOutputReleased(second.presentationTimeUs(), 16_000_000, true);
        codec.observer.onEndOfStream();

        assertTrue(observer.sample.toJson().toString().contains("\"droppedFrames\":1"));
        assertTrue(codec.stopped);
        assertTrue(codec.released);
        assertTrue(surfaces.closed);
    }

    @Test
    public void codecConfigurationFailureIsReportedAsDecoderEvidenceNotRunnerFailure() {
        RecordingCodec codec = new RecordingCodec();
        codec.configurationFailure = new IllegalStateException("unsupported mode");
        MediaCodecDeviceBenchmarkRoundExecutor executor =
            new MediaCodecDeviceBenchmarkRoundExecutor(
                ignored -> codec,
                ignored -> twoFrameVector(),
                new RecordingSurfaceFactory(),
                Runnable::run);
        RecordingRoundObserver observer = new RecordingRoundObserver();

        executor.start(round(), observer);

        assertTrue(observer.sample.toJson().toString().contains("\"configured\":false"));
        assertEquals(null, observer.failure);
        assertTrue(codec.released);
    }

    @Test
    public void codecCreationFailureStillClosesAlreadyCreatedPresentationSurface() {
        RecordingSurfaceFactory surfaces = new RecordingSurfaceFactory();
        MediaCodecDeviceBenchmarkRoundExecutor executor =
            new MediaCodecDeviceBenchmarkRoundExecutor(
                ignored -> { throw new IllegalStateException("codec unavailable"); },
                ignored -> twoFrameVector(),
                surfaces,
                Runnable::run);
        RecordingRoundObserver observer = new RecordingRoundObserver();

        executor.start(round(), observer);

        assertTrue(observer.sample.toJson().toString().contains("\"configured\":false"));
        assertTrue(surfaces.closed);
    }

    @Test
    public void cleanupFailureIsCountedAndDoesNotPreventSurfaceReleaseOrCompletion() {
        RecordingCodec codec = new RecordingCodec();
        codec.stopFailure = new IllegalStateException("stop failed");
        RecordingSurfaceFactory surfaces = new RecordingSurfaceFactory();
        MediaCodecDeviceBenchmarkRoundExecutor executor =
            new MediaCodecDeviceBenchmarkRoundExecutor(
                ignored -> codec,
                ignored -> twoFrameVector(),
                surfaces,
                Runnable::run);
        RecordingRoundObserver observer = new RecordingRoundObserver();

        executor.start(round(), observer);
        EncodedVideoSample first = codec.sampleProvider.nextSample();
        codec.observer.onInputQueued(first.presentationTimeUs(), 1_000_000);
        codec.observer.onOutputReleased(first.presentationTimeUs(), 6_000_000, true);
        surfaces.observer.onFramePresented(first.presentationTimeUs(), 10_000_000);
        codec.observer.onEndOfStream();

        assertTrue(observer.sample.toJson().toString().contains("\"outputErrors\":1"));
        assertTrue(codec.released);
        assertTrue(surfaces.closed);
    }

    private static BeaconBenchmarkHardwarePlan.DecoderRound round() {
        return BeaconBenchmarkHardwarePlan.parse(JsonParser.parseString(
            "{\"schemaVersion\":1,\"samplePowerBeforeAndAfterEachRound\":true," +
                "\"decoderRounds\":[{\"vectorId\":\"vector-a\",\"codec\":\"h264\",\"profile\":\"high\"," +
                "\"bitDepth\":8,\"width\":1280,\"height\":720,\"targetFps\":60,\"repetitionCount\":1}]}")
            .getAsJsonObject()).decoderRounds().get(0);
    }

    private static byte[] twoFrameVector() {
        return new byte[] {
            0, 0, 0, 1, 0x67, 1,
            0, 0, 0, 1, 0x65, 2,
            0, 0, 0, 1, 0x41, 3
        };
    }

    private static final class RecordingCodec implements EncodedVideoCodec {
        private EncodedVideoSampleProvider sampleProvider;
        private EncodedVideoCodecObserver observer;
        private RuntimeException configurationFailure;
        private RuntimeException stopFailure;
        private boolean stopped;
        private boolean released;

        @Override
        public void configure(
            EncodedVideoDecodeRequest request,
            Object surface,
            EncodedVideoSampleProvider sampleProvider) {
            throw new AssertionError("Benchmark configure overload was not used.");
        }

        @Override
        public void configure(
            EncodedVideoDecodeRequest request,
            Object surface,
            EncodedVideoSampleProvider sampleProvider,
            EncodedVideoCodecObserver observer) {
            if (configurationFailure != null) throw configurationFailure;
            this.sampleProvider = sampleProvider;
            this.observer = observer;
        }

        @Override public void start() { }
        @Override public void stop() {
            stopped = true;
            if (stopFailure != null) throw stopFailure;
        }
        @Override public void release() { released = true; }
    }

    private static final class RecordingSurfaceFactory implements BenchmarkPresentationSurfaceFactory {
        private Observer observer;
        private boolean closed;
        private Long presentationTimeUsOnClose;
        private long presentedAtNsOnClose;

        void presentOnClose(long presentationTimeUs, long presentedAtNs) {
            presentationTimeUsOnClose = presentationTimeUs;
            presentedAtNsOnClose = presentedAtNs;
        }

        @Override
        public BenchmarkPresentationSurface create(int width, int height, Observer observer) {
            this.observer = observer;
            return new BenchmarkPresentationSurface() {
                @Override public Object surface() { return new Object(); }
                @Override public void close() {
                    if (presentationTimeUsOnClose != null) {
                        observer.onFramePresented(
                            presentationTimeUsOnClose,
                            presentedAtNsOnClose);
                    }
                    closed = true;
                }
            };
        }
    }

    private static final class RecordingRoundObserver implements DeviceBenchmarkRoundExecutor.Observer {
        private BeaconBenchmarkCompletionRequest.DecoderSample sample;
        private Throwable failure;

        @Override
        public void onCompleted(BeaconBenchmarkCompletionRequest.DecoderSample sample) {
            this.sample = sample;
        }

        @Override
        public void onFailure(Throwable failure) {
            this.failure = failure;
        }
    }
}
