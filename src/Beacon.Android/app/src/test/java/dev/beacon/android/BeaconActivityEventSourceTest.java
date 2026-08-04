package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

public final class BeaconActivityEventSourceTest {
    @Test
    public void observerReceivesDurableHistoryBeforeLiveEvents() {
        BeaconActivityEventSource source = new BeaconActivityEventSource();
        source.publish(
            BeaconActivityEventSource.Kind.PRESENCE_ACTIVE,
            "Automatic connect",
            "beacon active: 200");

        List<BeaconActivityEventSource.Event> observed = new ArrayList<>();
        source.addObserver(observed::add);
        source.publish(
            BeaconActivityEventSource.Kind.BENCHMARK_COMPLETE,
            "automatic",
            "benchmark complete: 200");

        assertEquals(2, observed.size());
        assertEquals(BeaconActivityEventSource.Kind.PRESENCE_ACTIVE, observed.get(0).kind());
        assertEquals(BeaconActivityEventSource.Kind.BENCHMARK_COMPLETE, observed.get(1).kind());
        assertTrue(observed.get(1).sequence() > observed.get(0).sequence());
    }

    @Test
    public void videoFramesAreLiveOnlyAndRetainTransportGeneration() {
        BeaconActivityEventSource source = new BeaconActivityEventSource();
        List<BeaconActivityEventSource.Event> first = new ArrayList<>();
        source.addObserver(first::add);

        source.onRenderedFrameSent(7, 11, 33_000, 44_000);

        assertEquals(1, first.size());
        BeaconActivityEventSource.Event frame = first.get(0);
        assertEquals(BeaconActivityEventSource.Kind.VIDEO_FRAME, frame.kind());
        assertEquals(7, frame.streamGeneration());
        assertEquals(11, frame.frameSequence());
        assertEquals(33_000, frame.presentationTimeUs());
        assertEquals(44_000, frame.renderedAtUs());

        List<BeaconActivityEventSource.Event> replay = new ArrayList<>();
        source.addObserver(replay::add);
        assertTrue(replay.isEmpty());
    }
}
