package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

import static org.junit.Assert.assertEquals;

public final class BeaconVideoPipelineObserverSwitchTest {
    @Test
    public void decoratorCanDelayThenForwardProductionFeedback() {
        List<String> observed = new ArrayList<>();
        BeaconVideoPipeline.Observer production = new RecordingObserver(observed, "production");
        BeaconVideoPipelineObserverSwitch observer =
            new BeaconVideoPipelineObserverSwitch(production);

        observer.onFrameRendered(1, 0, 10);
        observer.install(downstream -> new BeaconVideoPipeline.Observer() {
            @Override
            public void onDecoderStateChanged(
                BeaconVideoPipeline.DecoderState state,
                int platformErrorCode) {
                downstream.onDecoderStateChanged(state, platformErrorCode);
            }

            @Override
            public void onQueueDepthChanged(int queuedAccessUnits, long droppedAccessUnits) {
                downstream.onQueueDepthChanged(queuedAccessUnits, droppedAccessUnits);
            }

            @Override
            public void onIdrRequired(long lastCompleteSequence) {
                downstream.onIdrRequired(lastCompleteSequence);
            }

            @Override
            public void onFrameRendered(
                long frameSequence,
                long presentationTimeUs,
                long renderedAtUs) {
                observed.add("evidence:" + frameSequence);
                downstream.onFrameRendered(
                    frameSequence,
                    presentationTimeUs,
                    renderedAtUs);
            }

            @Override
            public void onFailure(Throwable failure) {
                downstream.onFailure(failure);
            }
        });
        observer.onFrameRendered(2, 20, 30);
        observer.restoreProduction();
        observer.onFrameRendered(3, 40, 50);

        assertEquals(List.of(
            "production:1",
            "evidence:2",
            "production:2",
            "production:3"), observed);
    }

    private static final class RecordingObserver implements BeaconVideoPipeline.Observer {
        private final List<String> observed;
        private final String name;

        RecordingObserver(List<String> observed, String name) {
            this.observed = observed;
            this.name = name;
        }

        @Override
        public void onDecoderStateChanged(
            BeaconVideoPipeline.DecoderState state,
            int platformErrorCode) { }

        @Override
        public void onQueueDepthChanged(int queuedAccessUnits, long droppedAccessUnits) { }

        @Override
        public void onIdrRequired(long lastCompleteSequence) { }

        @Override
        public void onFrameRendered(
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) {
            observed.add(name + ":" + frameSequence);
        }

        @Override
        public void onFailure(Throwable failure) { }
    }
}
