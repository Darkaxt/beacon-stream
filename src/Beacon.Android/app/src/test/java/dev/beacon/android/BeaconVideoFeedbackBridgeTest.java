package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

import static org.junit.Assert.assertEquals;

public final class BeaconVideoFeedbackBridgeTest {
    @Test
    public void sendsOnlyForTheActiveBeaconGeneration() {
        RecordingSink sink = new RecordingSink();
        List<Throwable> failures = new ArrayList<>();
        RecordingObserver observer = new RecordingObserver();
        BeaconVideoFeedbackBridge bridge = new BeaconVideoFeedbackBridge(
            failures::add,
            observer);

        bridge.onQueueDepthChanged(1, 2);
        bridge.activate(7, sink);
        bridge.onQueueDepthChanged(3, 4);
        bridge.onDecoderStateChanged(BeaconVideoPipeline.DecoderState.FAILED, 321);
        bridge.onFrameRendered(8, 9, 10);
        bridge.onIdrRequired(8);
        bridge.deactivate();
        bridge.onQueueDepthChanged(5, 6);

        assertEquals(
            List.of(
                "queue:7:3:4",
                "decoder:7:FAILED:321",
                "rendered:7:8:9:10",
                "idr:7:8"),
            sink.events);
        assertEquals(
            List.of(
                "queue:7:3:4",
                "decoder:7:FAILED:321",
                "rendered:7:8:9:10",
                "idr:7:8"),
            observer.events);
        assertEquals(List.of(), failures);
    }

    private static final class RecordingObserver
        implements BeaconVideoFeedbackBridge.Observer {
        private final List<String> events = new ArrayList<>();

        @Override
        public void onQueueDepthSent(
            long generation,
            int queuedAccessUnits,
            long droppedAccessUnits) {
            events.add("queue:" + generation + ":" + queuedAccessUnits + ":" +
                droppedAccessUnits);
        }

        @Override
        public void onDecoderStateSent(
            long generation,
            BeaconStreamCore.DecoderState state,
            int platformErrorCode) {
            events.add("decoder:" + generation + ":" + state + ":" +
                platformErrorCode);
        }

        @Override
        public void onRenderedFrameSent(
            long generation,
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) {
            events.add("rendered:" + generation + ":" + frameSequence + ":" +
                presentationTimeUs + ":" + renderedAtUs);
        }

        @Override
        public void onIdrRequested(
            long generation,
            long lastCompleteSequence) {
            events.add("idr:" + generation + ":" + lastCompleteSequence);
        }
    }

    private static final class RecordingSink implements BeaconVideoFeedbackBridge.Sink {
        private final List<String> events = new ArrayList<>();

        @Override
        public void sendQueueDepth(
            long generation,
            int queuedAccessUnits,
            long droppedAccessUnits) {
            events.add("queue:" + generation + ":" + queuedAccessUnits + ":" +
                droppedAccessUnits);
        }

        @Override
        public void sendDecoderState(
            long generation,
            BeaconStreamCore.DecoderState state,
            int platformErrorCode) {
            events.add("decoder:" + generation + ":" + state + ":" +
                platformErrorCode);
        }

        @Override
        public void sendRenderedFrame(
            long generation,
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) {
            events.add("rendered:" + generation + ":" + frameSequence + ":" +
                presentationTimeUs + ":" + renderedAtUs);
        }

        @Override
        public void requestDecoderIdr(
            long generation,
            long lastCompleteSequence) {
            events.add("idr:" + generation + ":" + lastCompleteSequence);
        }
    }
}
