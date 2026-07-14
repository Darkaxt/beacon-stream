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

    private final FailureObserver failureObserver;
    private Sink sink;
    private long generation;

    BeaconVideoFeedbackBridge(FailureObserver failureObserver) {
        if (failureObserver == null) {
            throw new IllegalArgumentException("Video feedback failure observer is required.");
        }
        this.failureObserver = failureObserver;
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
        active.sink.sendDecoderState(
            active.generation,
            switch (state) {
                case READY -> BeaconStreamCore.DecoderState.READY;
                case AWAITING_IDR -> BeaconStreamCore.DecoderState.AWAITING_IDR;
                case FAILED -> BeaconStreamCore.DecoderState.FAILED;
            },
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
    }

    @Override
    public void onIdrRequired(long lastCompleteSequence) {
        ActiveSink active = activeSink();
        if (active == null) return;
        active.sink.requestDecoderIdr(
            active.generation,
            lastCompleteSequence);
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
