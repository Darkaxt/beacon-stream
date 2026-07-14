package dev.beacon.android;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

final class BeaconVideoSession implements BeaconViewModel.VideoSession {
    private final ExecutorService decoderExecutor;
    private final BeaconVideoFeedbackBridge feedback;
    private final BeaconVideoPipeline pipeline;
    private boolean closed;

    BeaconVideoSession(
        EncodedVideoSurfaceProvider surfaceProvider,
        BeaconVideoFeedbackBridge.FailureObserver failureObserver) {
        if (surfaceProvider == null || failureObserver == null) {
            throw new IllegalArgumentException("Beacon video session dependencies are required.");
        }
        decoderExecutor = Executors.newSingleThreadExecutor(
            action -> new Thread(action, "beacon-video-decoder"));
        feedback = new BeaconVideoFeedbackBridge(failureObserver);
        pipeline = new BeaconVideoPipeline(
            new AndroidMediaCodecFactory(),
            surfaceProvider,
            3,
            decoderExecutor,
            feedback);
    }

    @Override
    public synchronized void start(
        BeaconStreamCore streamCore,
        long generation,
        BeaconStreamSession.SelectedVideo video) {
        if (closed) {
            throw new IllegalStateException("Beacon video session is closed.");
        }
        if (streamCore == null || video == null) {
            throw new IllegalArgumentException("Beacon video start is incomplete.");
        }
        feedback.activate(generation, new CoreFeedbackSink(streamCore));
        pipeline.start(
            video.codec(),
            video.width(),
            video.height(),
            framesPerSecond(video));
    }

    @Override
    public synchronized void stop() {
        if (closed) return;
        feedback.deactivate();
        pipeline.stop();
    }

    @Override
    public void onFrame(BeaconStreamCore.EncodedFrame frame) {
        pipeline.onFrame(frame);
    }

    @Override
    public synchronized void close() {
        if (closed) return;
        closed = true;
        feedback.deactivate();
        pipeline.close();
        decoderExecutor.shutdown();
    }

    private static int framesPerSecond(BeaconStreamSession.SelectedVideo video) {
        long numerator = video.framesPerSecondNumerator();
        long denominator = video.framesPerSecondDenominator();
        long rounded = (numerator + denominator / 2) / denominator;
        if (rounded <= 0 || rounded > Integer.MAX_VALUE) {
            throw new IllegalArgumentException("Selected video frame rate is invalid.");
        }
        return (int) rounded;
    }

    private static final class CoreFeedbackSink
        implements BeaconVideoFeedbackBridge.Sink {
        private final BeaconStreamCore streamCore;

        CoreFeedbackSink(BeaconStreamCore streamCore) {
            this.streamCore = streamCore;
        }

        @Override
        public void sendQueueDepth(
            long generation,
            int queuedAccessUnits,
            long droppedAccessUnits) {
            streamCore.sendQueueDepthFeedback(
                generation, queuedAccessUnits, droppedAccessUnits);
        }

        @Override
        public void sendDecoderState(
            long generation,
            BeaconStreamCore.DecoderState state,
            int platformErrorCode) {
            streamCore.sendDecoderFeedback(generation, state, platformErrorCode);
        }

        @Override
        public void sendRenderedFrame(
            long generation,
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) {
            streamCore.sendRenderedFrameFeedback(
                generation,
                frameSequence,
                presentationTimeUs,
                renderedAtUs);
        }

        @Override
        public void requestDecoderIdr(
            long generation,
            long lastCompleteSequence) {
            streamCore.requestDecoderIdr(generation, lastCompleteSequence);
        }
    }
}
