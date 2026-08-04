package dev.beacon.android;

import org.junit.Test;

import java.nio.ByteBuffer;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Queue;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertSame;

public final class BeaconVideoPipelineTest {
    @Test
    public void configuredIdrStartsDecodeAndRenderedFeedbackPreservesFrameIdentity() {
        RecordingCodec codec = new RecordingCodec();
        RecordingPipelineObserver observer = new RecordingPipelineObserver();
        BeaconVideoPipeline pipeline = pipeline(
            new SequenceCodecFactory(codec),
            new RecordingSurfaceProvider(new Object()),
            observer);

        pipeline.start("h264", 1280, 720, 60);
        assertEquals(List.of(state(BeaconVideoPipeline.DecoderState.AWAITING_IDR, 0)), observer.states);

        pipeline.onFrame(frame(7, true, true));
        EncodedVideoSample sample = codec.sampleProvider.nextSample();
        codec.observer.onInputQueued(
            sample.sequence(), sample.presentationTimeUs(), 1_000_000_000);
        codec.observer.onOutputReleased(
            sample.sequence(), sample.presentationTimeUs(), 2_000_000_000, true);

        assertEquals(List.of(), observer.renderedFrames);
        codec.observer.onFrameRendered(
            sample.sequence(), sample.presentationTimeUs(), 2_100_000_000);

        assertEquals(7, sample.sequence());
        assertEquals(
            Arrays.asList(
                state(BeaconVideoPipeline.DecoderState.AWAITING_IDR, 0),
                state(BeaconVideoPipeline.DecoderState.READY, 0)),
            observer.states);
        assertEquals(List.of(new RenderedFrame(7, 7_000, 2_100_000)), observer.renderedFrames);
        pipeline.close();
    }

    @Test
    public void selectedHdrVideoCarriesEveryFieldIntoDecoderRequest() {
        RecordingCodec codec = new RecordingCodec();
        BeaconVideoPipeline pipeline = pipeline(
            new SequenceCodecFactory(codec),
            new RecordingSurfaceProvider(new Object()),
            new RecordingPipelineObserver());
        byte[] staticInfo = new byte[25];
        staticInfo[24] = 7;

        pipeline.start(new BeaconStreamSession.SelectedVideo(
            "hevc", 3840, 2160, 60, 1, "hdr10", "hevcMain10", 10,
            "bt2020", "pq", "bt2020NonConstantLuminance", "limited",
            staticInfo, true));

        assertEquals("hevcMain10", codec.request.profile());
        assertEquals(10, codec.request.bitDepth());
        assertEquals("bt2020", codec.request.colorPrimaries());
        assertEquals("pq", codec.request.transferFunction());
        assertEquals("bt2020NonConstantLuminance", codec.request.matrixCoefficients());
        assertEquals("limited", codec.request.colorRange());
        assertEquals(7, codec.request.hdrStaticInfo()[24]);
        assertEquals(true, codec.request.hdrStaticInfoInBitstream());
        pipeline.close();
    }

    @Test
    public void decoderFailureReportsCodeDropsQueueAndRecreatesDecoder() {
        RecordingCodec first = new RecordingCodec();
        RecordingCodec second = new RecordingCodec();
        RecordingPipelineObserver observer = new RecordingPipelineObserver();
        BeaconVideoPipeline pipeline = pipeline(
            new SequenceCodecFactory(first, second),
            new RecordingSurfaceProvider(new Object()),
            observer);
        pipeline.start("h264", 1280, 720, 60);
        pipeline.onFrame(frame(10, true, true));
        pipeline.onFrame(frame(11, false, false));

        first.observer.onError(new EncodedVideoCodecFailure(321, "decoder failed"));

        assertEquals(1, first.stopCount);
        assertEquals(1, first.releaseCount);
        assertEquals(1, second.startCount);
        assertEquals(List.of(11L), observer.idrRequests);
        assertEquals(new QueueState(0, 2), observer.queueStates.get(observer.queueStates.size() - 1));
        assertEquals(
            Arrays.asList(
                state(BeaconVideoPipeline.DecoderState.AWAITING_IDR, 0),
                state(BeaconVideoPipeline.DecoderState.READY, 0),
                state(BeaconVideoPipeline.DecoderState.FAILED, 321),
                state(BeaconVideoPipeline.DecoderState.AWAITING_IDR, 0)),
            observer.states);

        pipeline.onFrame(frame(12, true, true));
        assertEquals(12, second.sampleProvider.nextSample().sequence());
        pipeline.close();
    }

    @Test
    public void hdrOutputFormatMismatchStopsWithoutDecoderRecovery() {
        RecordingCodec first = new RecordingCodec();
        RecordingCodec second = new RecordingCodec();
        RecordingPipelineObserver observer = new RecordingPipelineObserver();
        BeaconVideoPipeline pipeline = pipeline(
            new SequenceCodecFactory(first, second),
            new RecordingSurfaceProvider(new Object()),
            observer);
        pipeline.start(new BeaconStreamSession.SelectedVideo(
            "hevc", 3840, 2160, 60, 1, "hdr10", "hevcMain10", 10,
            "bt2020", "pq", "bt2020NonConstantLuminance", "limited",
            new byte[25], true));

        first.observer.onError(new EncodedVideoOutputFormatMismatch(
            "MediaCodec output format does not match HDR10."));

        assertEquals(1, first.stopCount);
        assertEquals(1, first.releaseCount);
        assertEquals(0, second.startCount);
        assertEquals(1, observer.failures.size());
        pipeline.close();
    }

    @Test
    public void surfaceLifecycleStopsAndRestartsDecoderWithoutLeakingCodec() {
        RecordingCodec first = new RecordingCodec();
        RecordingCodec second = new RecordingCodec();
        RecordingSurfaceProvider surfaces = new RecordingSurfaceProvider(new Object());
        BeaconVideoPipeline pipeline = pipeline(
            new SequenceCodecFactory(first, second),
            surfaces,
            new RecordingPipelineObserver());
        pipeline.start("h264", 1280, 720, 60);

        surfaces.destroy();

        assertEquals(1, first.stopCount);
        assertEquals(1, first.releaseCount);
        assertEquals(0, second.startCount);

        Object replacement = new Object();
        surfaces.available(replacement);

        assertEquals(1, second.startCount);
        assertSame(replacement, second.surface);
        pipeline.close();
        pipeline.close();
        assertEquals(1, second.stopCount);
        assertEquals(1, second.releaseCount);
    }

    @Test
    public void explicitStopThenReconnectOwnsEachCodecExactlyOnce() {
        RecordingCodec first = new RecordingCodec();
        RecordingCodec second = new RecordingCodec();
        BeaconVideoPipeline pipeline = pipeline(
            new SequenceCodecFactory(first, second),
            new RecordingSurfaceProvider(new Object()),
            new RecordingPipelineObserver());
        pipeline.start("h264", 1280, 720, 60);

        pipeline.stop();
        pipeline.stop();
        pipeline.start("h264", 1280, 720, 60);
        pipeline.close();

        assertEquals(1, first.stopCount);
        assertEquals(1, first.releaseCount);
        assertEquals(1, second.stopCount);
        assertEquals(1, second.releaseCount);
    }

    @Test
    public void decoderLifecycleNeverRunsInlineOnTheCallingThread() {
        RecordingCodec codec = new RecordingCodec();
        ManualExecutor decoderExecutor = new ManualExecutor();
        BeaconVideoPipeline pipeline = new BeaconVideoPipeline(
            new SequenceCodecFactory(codec),
            new RecordingSurfaceProvider(new Object()),
            3,
            decoderExecutor,
            new RecordingPipelineObserver());

        pipeline.start("h264", 1280, 720, 60);

        assertEquals(0, codec.startCount);
        decoderExecutor.runNext();
        assertEquals(1, codec.startCount);

        pipeline.stop();
        assertEquals(0, codec.stopCount);
        decoderExecutor.runNext();
        assertEquals(1, codec.stopCount);
        assertEquals(1, codec.releaseCount);
    }

    @Test
    public void replacingDecoderRejectsRenderedCallbacksFromPreviousRequestImmediately() {
        RecordingCodec first = new RecordingCodec();
        RecordingCodec second = new RecordingCodec();
        ManualExecutor decoderExecutor = new ManualExecutor();
        RecordingPipelineObserver observer = new RecordingPipelineObserver();
        BeaconVideoPipeline pipeline = new BeaconVideoPipeline(
            new SequenceCodecFactory(first, second),
            new RecordingSurfaceProvider(new Object()),
            3,
            decoderExecutor,
            observer);
        pipeline.start("h264", 1280, 720, 60);
        decoderExecutor.runNext();

        pipeline.start("h264", 1920, 1080, 60);
        first.observer.onFrameRendered(7, 7_000, 8_000_000);

        assertEquals(List.of(), observer.renderedFrames);
        decoderExecutor.runNext();
        pipeline.close();
        decoderExecutor.runNext();
    }

    private static BeaconVideoPipeline pipeline(
        EncodedVideoCodecFactory codecs,
        EncodedVideoSurfaceProvider surfaces,
        RecordingPipelineObserver observer) {
        return new BeaconVideoPipeline(codecs, surfaces, 3, Runnable::run, observer);
    }

    private static BeaconStreamCore.EncodedFrame frame(
        long sequence,
        boolean idr,
        boolean codecConfiguration) {
        return new BeaconStreamCore.EncodedFrame(
            directBuffer((int) sequence),
            sequence * 1_000,
            sequence,
            idr,
            codecConfiguration);
    }

    private static ByteBuffer directBuffer(int... values) {
        ByteBuffer buffer = ByteBuffer.allocateDirect(values.length);
        for (int value : values) buffer.put((byte) value);
        buffer.flip();
        return buffer;
    }

    private static DecoderStatus state(BeaconVideoPipeline.DecoderState state, int code) {
        return new DecoderStatus(state, code);
    }

    private record DecoderStatus(BeaconVideoPipeline.DecoderState state, int code) { }
    private record QueueState(int queued, long dropped) { }
    private record RenderedFrame(long sequence, long presentationTimeUs, long renderedAtUs) { }

    private static final class RecordingPipelineObserver implements BeaconVideoPipeline.Observer {
        private final List<DecoderStatus> states = new ArrayList<>();
        private final List<QueueState> queueStates = new ArrayList<>();
        private final List<Long> idrRequests = new ArrayList<>();
        private final List<RenderedFrame> renderedFrames = new ArrayList<>();
        private final List<Throwable> failures = new ArrayList<>();

        @Override
        public void onDecoderStateChanged(
            BeaconVideoPipeline.DecoderState state,
            int platformErrorCode) {
            states.add(new DecoderStatus(state, platformErrorCode));
        }

        @Override
        public void onQueueDepthChanged(int queuedAccessUnits, long droppedAccessUnits) {
            queueStates.add(new QueueState(queuedAccessUnits, droppedAccessUnits));
        }

        @Override
        public void onIdrRequired(long lastCompleteSequence) {
            idrRequests.add(lastCompleteSequence);
        }

        @Override
        public void onFrameRendered(
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) {
            renderedFrames.add(new RenderedFrame(
                frameSequence,
                presentationTimeUs,
                renderedAtUs));
        }

        @Override public void onFailure(Throwable failure) { failures.add(failure); }
    }

    private static final class RecordingSurfaceProvider implements EncodedVideoSurfaceProvider {
        private Object surface;
        private Observer observer = Observer.noOp();

        RecordingSurfaceProvider(Object surface) {
            this.surface = surface;
        }

        @Override public Object currentSurface() { return surface; }

        @Override public void setObserver(Observer observer) {
            this.observer = observer;
        }

        void destroy() {
            surface = null;
            observer.onSurfaceDestroyed();
        }

        void available(Object replacement) {
            surface = replacement;
            observer.onSurfaceAvailable();
        }
    }

    private static final class SequenceCodecFactory implements EncodedVideoCodecFactory {
        private final Queue<RecordingCodec> codecs = new ArrayDeque<>();

        SequenceCodecFactory(RecordingCodec... codecs) {
            this.codecs.addAll(Arrays.asList(codecs));
        }

        @Override public EncodedVideoCodec create(String codec) { return codecs.remove(); }
    }

    private static final class RecordingCodec implements EncodedVideoCodec {
        private EncodedVideoDecodeRequest request;
        private EncodedVideoSampleProvider sampleProvider;
        private EncodedVideoCodecObserver observer;
        private Object surface;
        private int startCount;
        private int stopCount;
        private int releaseCount;

        @Override
        public void configure(
            EncodedVideoDecodeRequest request,
            Object surface,
            EncodedVideoSampleProvider sampleProvider) {
            throw new AssertionError("Observer-aware configure overload was not used.");
        }

        @Override
        public void configure(
            EncodedVideoDecodeRequest request,
            Object surface,
            EncodedVideoSampleProvider sampleProvider,
            EncodedVideoCodecObserver observer) {
            this.request = request;
            this.surface = surface;
            this.sampleProvider = sampleProvider;
            this.observer = observer;
        }

        @Override public void start() { startCount++; }
        @Override public void stop() { stopCount++; }
        @Override public void release() { releaseCount++; }
    }

    private static final class ManualExecutor implements java.util.concurrent.Executor {
        private final Queue<Runnable> actions = new ArrayDeque<>();

        @Override public void execute(Runnable action) { actions.add(action); }

        void runNext() { actions.remove().run(); }
    }
}
