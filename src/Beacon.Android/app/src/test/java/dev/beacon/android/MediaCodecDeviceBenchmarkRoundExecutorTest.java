package dev.beacon.android;

import com.google.gson.JsonParser;

import org.junit.Test;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
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
        codec.observer.onInputQueued(first.sequence(), first.presentationTimeUs(), 1_000_000);
        codec.observer.onInputQueued(second.sequence(), second.presentationTimeUs(), 11_000_000);
        codec.observer.onOutputReleased(
            first.sequence(), first.presentationTimeUs(), 6_000_000, true);
        surfaces.observer.onFramePresented(first.presentationTimeUs(), 10_000_000);
        codec.observer.onOutputReleased(
            second.sequence(), second.presentationTimeUs(), 16_000_000, true);
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
        codec.observer.onInputQueued(first.sequence(), first.presentationTimeUs(), 1_000_000);
        codec.observer.onInputQueued(second.sequence(), second.presentationTimeUs(), 11_000_000);
        codec.observer.onOutputReleased(
            first.sequence(), first.presentationTimeUs(), 6_000_000, true);
        surfaces.observer.onFramePresented(first.presentationTimeUs(), 10_000_000);
        codec.observer.onOutputReleased(
            second.sequence(), second.presentationTimeUs(), 16_000_000, true);
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
    public void cleanupFailuresDoNotBecomeDecoderOutputErrors() {
        RecordingCodec codec = new RecordingCodec();
        codec.stopFailure = new IllegalStateException("stop failed");
        codec.releaseFailure = new IllegalStateException("release failed");
        RecordingSurfaceFactory surfaces = new RecordingSurfaceFactory();
        List<String> cleanupDiagnostics = new ArrayList<>();
        MediaCodecDeviceBenchmarkRoundExecutor executor =
            new MediaCodecDeviceBenchmarkRoundExecutor(
                ignored -> codec,
                ignored -> twoFrameVector(),
                surfaces,
                Runnable::run,
                (diagnostic, failure) -> cleanupDiagnostics.add(
                    diagnostic + ": " + failure.getMessage()));
        RecordingRoundObserver observer = new RecordingRoundObserver();

        executor.start(round(), observer);
        EncodedVideoSample first = codec.sampleProvider.nextSample();
        codec.observer.onInputQueued(first.sequence(), first.presentationTimeUs(), 1_000_000);
        codec.observer.onOutputReleased(
            first.sequence(), first.presentationTimeUs(), 6_000_000, true);
        surfaces.observer.onFramePresented(first.presentationTimeUs(), 10_000_000);
        codec.observer.onEndOfStream();

        assertTrue(observer.sample.toJson().toString().contains("\"outputErrors\":0"));
        assertEquals(List.of(
            "operation=codec.stop codec=h264 profile=high size=1280x720 targetFps=60: stop failed",
            "operation=codec.release codec=h264 profile=high size=1280x720 targetFps=60: release failed"),
            cleanupDiagnostics);
        assertTrue(codec.released);
        assertTrue(surfaces.closed);
    }

    @Test
    public void codecCallbackFailureRemainsAReportedDecoderOutputError() {
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
        codec.observer.onError(new IllegalStateException("decoder callback failed"));

        assertTrue(observer.sample.toJson().toString().contains("\"outputErrors\":1"));
        assertTrue(codec.stopped);
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
        byte[] first = new byte[] {
            0, 0, 0, 1, 0x67, 1,
            0, 0, 0, 1, 0x65, 2
        };
        byte[] second = new byte[] { 0, 0, 0, 1, 0x41, 3 };
        byte[] magic = "BEACONAU1\n".getBytes(StandardCharsets.US_ASCII);
        ByteBuffer result = ByteBuffer.allocate(
            magic.length + Integer.BYTES * 3 + first.length + second.length)
            .order(ByteOrder.LITTLE_ENDIAN);
        result.put(magic);
        result.putInt(2);
        result.putInt(first.length).put(first);
        result.putInt(second.length).put(second);
        return result.array();
    }

    private static final class RecordingCodec implements EncodedVideoCodec {
        private EncodedVideoSampleProvider sampleProvider;
        private EncodedVideoCodecObserver observer;
        private RuntimeException configurationFailure;
        private RuntimeException stopFailure;
        private RuntimeException releaseFailure;
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
        @Override public void release() {
            released = true;
            if (releaseFailure != null) throw releaseFailure;
        }
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
