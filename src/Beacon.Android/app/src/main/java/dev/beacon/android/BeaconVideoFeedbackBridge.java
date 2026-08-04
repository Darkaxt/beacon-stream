package dev.beacon.android;

final class BeaconVideoFeedbackBridge implements BeaconVideoPipeline.Observer {
    interface Sink {
        void sendQueueDepth(
            long generation,
            int queuedAccessUnits,
            long droppedAccessUnits);
        void sendDecoderState(
            long generation,
            BeaconStreamCore.DecoderState state,
            int platformErrorCode);
        void sendRenderedFrame(
            long generation,
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs);
        void requestDecoderIdr(
            long generation,
            long lastCompleteSequence);
    }

    interface FailureObserver {
        void onFailure(Throwable failure);
    }

    interface Observer {
        default void onQueueDepthSent(
            long generation,
            int queuedAccessUnits,
            long droppedAccessUnits) { }
        default void onDecoderStateSent(
            long generation,
            BeaconStreamCore.DecoderState state,
            int platformErrorCode) { }
        default void onRenderedFrameSent(
            long generation,
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) { }
        default void onIdrRequested(
            long generation,
            long lastCompleteSequence) { }

        static Observer noOp() {
            return new Observer() { };
        }
    }

    private final FailureObserver failureObserver;
    private final Observer observer;
    private Sink sink;
    private long generation;

    BeaconVideoFeedbackBridge(FailureObserver failureObserver) {
        this(failureObserver, Observer.noOp());
    }

    BeaconVideoFeedbackBridge(
        FailureObserver failureObserver,
        Observer observer) {
        if (failureObserver == null || observer == null) {
            throw new IllegalArgumentException("Video feedback failure observer is required.");
        }
        this.failureObserver = failureObserver;
        this.observer = observer;
    }

    synchronized void activate(long generation, Sink sink) {
        if (generation <= 0 || sink == null) {
            throw new IllegalArgumentException("Active video feedback generation is required.");
        }
        this.generation = generation;
        this.sink = sink;
    }

    synchronized void deactivate() {
        generation = 0;
        sink = null;
    }

    @Override
    public void onDecoderStateChanged(
        BeaconVideoPipeline.DecoderState state,
        int platformErrorCode) {
        ActiveSink active = activeSink();
        if (active == null) return;
        BeaconStreamCore.DecoderState sentState =
            switch (state) {
                case READY -> BeaconStreamCore.DecoderState.READY;
                case AWAITING_IDR -> BeaconStreamCore.DecoderState.AWAITING_IDR;
                case FAILED -> BeaconStreamCore.DecoderState.FAILED;
            };
        active.sink.sendDecoderState(
            active.generation,
            sentState,
            platformErrorCode);
        observer.onDecoderStateSent(
            active.generation,
            sentState,
            platformErrorCode);
    }

    @Override
    public void onQueueDepthChanged(
        int queuedAccessUnits,
        long droppedAccessUnits) {
        ActiveSink active = activeSink();
        if (active == null) return;
        active.sink.sendQueueDepth(
            active.generation,
            queuedAccessUnits,
            droppedAccessUnits);
        observer.onQueueDepthSent(
            active.generation,
            queuedAccessUnits,
            droppedAccessUnits);
    }

    @Override
    public void onIdrRequired(long lastCompleteSequence) {
        ActiveSink active = activeSink();
        if (active == null) return;
        active.sink.requestDecoderIdr(
            active.generation,
            lastCompleteSequence);
        observer.onIdrRequested(active.generation, lastCompleteSequence);
    }

    @Override
    public void onFrameRendered(
        long frameSequence,
        long presentationTimeUs,
        long renderedAtUs) {
        ActiveSink active = activeSink();
        if (active == null) return;
        active.sink.sendRenderedFrame(
            active.generation,
            frameSequence,
            presentationTimeUs,
            renderedAtUs);
        observer.onRenderedFrameSent(
            active.generation,
            frameSequence,
            presentationTimeUs,
            renderedAtUs);
    }

    @Override
    public void onFailure(Throwable failure) {
        failureObserver.onFailure(failure);
    }

    private synchronized ActiveSink activeSink() {
        return generation == 0 || sink == null
            ? null
            : new ActiveSink(generation, sink);
    }

    private record ActiveSink(long generation, Sink sink) { }
}
