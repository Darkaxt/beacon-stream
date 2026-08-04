package dev.beacon.android;

import org.junit.Test;

import java.util.OptionalLong;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;

public final class EncodedFramePresentationTrackerTest {
    @Test
    public void preservesFrameSequenceForEachQueuedPresentationTimestamp() {
        EncodedFramePresentationTracker tracker = new EncodedFramePresentationTracker();
        tracker.track(7, 100);
        tracker.track(8, 100);
        tracker.track(9, 200);

        assertEquals(7, tracker.markOutputReleased(100).orElseThrow());
        assertEquals(8, tracker.markOutputReleased(100).orElseThrow());
        assertFalse(tracker.markOutputReleased(100).isPresent());
        assertEquals(7, tracker.takeRendered(100).orElseThrow());
        assertEquals(8, tracker.takeRendered(100).orElseThrow());
        assertFalse(tracker.takeRendered(100).isPresent());
        assertEquals(9, tracker.markOutputReleased(200).orElseThrow());
        assertEquals(9, tracker.takeRendered(200).orElseThrow());
    }

    @Test
    public void failedQueueAndCloseRemoveUnrenderableMappings() {
        EncodedFramePresentationTracker tracker = new EncodedFramePresentationTracker();
        tracker.track(10, 300);
        tracker.track(11, 300);

        tracker.discard(11, 300);
        assertEquals(10, tracker.markOutputReleased(300).orElseThrow());
        assertEquals(10, tracker.takeRendered(300).orElseThrow());
        tracker.track(12, 400);
        tracker.clear();

        assertEquals(OptionalLong.empty(), tracker.markOutputReleased(400));
        assertEquals(OptionalLong.empty(), tracker.takeRendered(400));
    }
}
