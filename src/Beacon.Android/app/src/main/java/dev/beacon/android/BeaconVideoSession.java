package dev.beacon.android;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.function.Consumer;

final class BeaconVideoSession implements BeaconViewModel.VideoSession {
    private final ExecutorService decoderExecutor;
    private final BeaconVideoFeedbackBridge feedback;
    private final BeaconVideoPipeline pipeline;
    private final HdrWindowModeController windowMode;
    private boolean closed;

    BeaconVideoSession(
        EncodedVideoSurfaceProvider surfaceProvider,
        BeaconVideoFeedbackBridge.FailureObserver failureObserver) {
        this(
            surfaceProvider,
            failureObserver,
            BeaconVideoFeedbackBridge.Observer.noOp());
    }

    BeaconVideoSession(
        EncodedVideoSurfaceProvider surfaceProvider,
        BeaconVideoFeedbackBridge.FailureObserver failureObserver,
        BeaconVideoFeedbackBridge.Observer observer) {
        this(
            surfaceProvider,
            failureObserver,
            observer,
            ignored -> { },
            HdrWindowModeController.noOp());
    }

    BeaconVideoSession(
        EncodedVideoSurfaceProvider surfaceProvider,
        BeaconVideoFeedbackBridge.FailureObserver failureObserver,
        BeaconVideoFeedbackBridge.Observer observer,
        Consumer<BeaconVideoPipelineObserverSwitch> observerRegistration) {
        this(
            surfaceProvider,
            failureObserver,
            observer,
            observerRegistration,
            HdrWindowModeController.noOp());
    }

    BeaconVideoSession(
        EncodedVideoSurfaceProvider surfaceProvider,
        BeaconVideoFeedbackBridge.FailureObserver failureObserver,
        BeaconVideoFeedbackBridge.Observer observer,
        Consumer<BeaconVideoPipelineObserverSwitch> observerRegistration,
        HdrWindowModeController windowMode) {
        if (surfaceProvider == null || failureObserver == null || observer == null) {
            throw new IllegalArgumentException("Beacon video session dependencies are required.");
        }
        if (observerRegistration == null || windowMode == null) {
            throw new IllegalArgumentException("Beacon video observer registration is required.");
        }
        this.windowMode = windowMode;
        decoderExecutor = Executors.newSingleThreadExecutor(
            action -> new Thread(action, "beacon-video-decoder"));
        feedback = new BeaconVideoFeedbackBridge(failure -> {
            windowMode.setHdrEnabled(false);
            failureObserver.onFailure(failure);
        }, observer);
        BeaconVideoPipelineObserverSwitch pipelineObserver =
            new BeaconVideoPipelineObserverSwitch(feedback);
        observerRegistration.accept(pipelineObserver);
        pipeline = new BeaconVideoPipeline(
            new AndroidMediaCodecFactory(),
            surfaceProvider,
            3,
            decoderExecutor,
            pipelineObserver);
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
        boolean hdr = "hdr10".equalsIgnoreCase(video.dynamicRange());
        windowMode.setHdrEnabled(hdr);
        try {
            pipeline.start(video);
        } catch (RuntimeException failure) {
            windowMode.setHdrEnabled(false);
            throw failure;
        }
    }

    @Override
    public synchronized void stop() {
        if (closed) return;
        feedback.deactivate();
        pipeline.stop();
        windowMode.setHdrEnabled(false);
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
        windowMode.setHdrEnabled(false);
        decoderExecutor.shutdown();
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
