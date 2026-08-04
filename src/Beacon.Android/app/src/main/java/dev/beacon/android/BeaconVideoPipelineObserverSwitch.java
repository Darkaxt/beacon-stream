package dev.beacon.android;

final class BeaconVideoPipelineObserverSwitch implements BeaconVideoPipeline.Observer {
    interface Decorator {
        BeaconVideoPipeline.Observer create(BeaconVideoPipeline.Observer downstream);
    }

    private final BeaconVideoPipeline.Observer production;
    private BeaconVideoPipeline.Observer active;

    BeaconVideoPipelineObserverSwitch(BeaconVideoPipeline.Observer production) {
        if (production == null) {
            throw new IllegalArgumentException("Production video observer is required.");
        }
        this.production = production;
        active = production;
    }

    synchronized void install(Decorator decorator) {
        if (decorator == null) {
            throw new IllegalArgumentException("Video observer decorator is required.");
        }
        BeaconVideoPipeline.Observer replacement = decorator.create(production);
        if (replacement == null) {
            throw new IllegalArgumentException("Video observer decorator returned no observer.");
        }
        active = replacement;
    }

    synchronized void restoreProduction() {
        active = production;
    }

    @Override
    public void onDecoderStateChanged(
        BeaconVideoPipeline.DecoderState state,
        int platformErrorCode) {
        target().onDecoderStateChanged(state, platformErrorCode);
    }

    @Override
    public void onQueueDepthChanged(int queuedAccessUnits, long droppedAccessUnits) {
        target().onQueueDepthChanged(queuedAccessUnits, droppedAccessUnits);
    }

    @Override
    public void onIdrRequired(long lastCompleteSequence) {
        target().onIdrRequired(lastCompleteSequence);
    }

    @Override
    public void onFrameRendered(
        long frameSequence,
        long presentationTimeUs,
        long renderedAtUs) {
        target().onFrameRendered(frameSequence, presentationTimeUs, renderedAtUs);
    }

    @Override
    public void onFailure(Throwable failure) {
        target().onFailure(failure);
    }

    private synchronized BeaconVideoPipeline.Observer target() {
        return active;
    }
}
