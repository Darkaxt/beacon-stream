package dev.beacon.android;

import java.util.ArrayDeque;
import java.util.HashMap;
import java.util.Iterator;
import java.util.Map;
import java.util.OptionalLong;

final class EncodedFramePresentationTracker {
    private final Map<Long, ArrayDeque<FrameIdentity>> framesByPresentationTime =
        new HashMap<>();

    synchronized void track(long sequence, long presentationTimeUs) {
        framesByPresentationTime
            .computeIfAbsent(presentationTimeUs, ignored -> new ArrayDeque<>())
            .add(new FrameIdentity(sequence));
    }

    synchronized void discard(long sequence, long presentationTimeUs) {
        ArrayDeque<FrameIdentity> frames = framesByPresentationTime.get(presentationTimeUs);
        if (frames == null) return;
        frames.removeIf(frame -> frame.sequence == sequence);
        if (frames.isEmpty()) framesByPresentationTime.remove(presentationTimeUs);
    }

    synchronized OptionalLong markOutputReleased(long presentationTimeUs) {
        ArrayDeque<FrameIdentity> frames = framesByPresentationTime.get(presentationTimeUs);
        if (frames == null) return OptionalLong.empty();
        for (FrameIdentity frame : frames) {
            if (!frame.outputReleased) {
                frame.outputReleased = true;
                return OptionalLong.of(frame.sequence);
            }
        }
        return OptionalLong.empty();
    }

    synchronized OptionalLong takeRendered(long presentationTimeUs) {
        ArrayDeque<FrameIdentity> frames = framesByPresentationTime.get(presentationTimeUs);
        if (frames == null) return OptionalLong.empty();
        Iterator<FrameIdentity> iterator = frames.iterator();
        while (iterator.hasNext()) {
            FrameIdentity frame = iterator.next();
            if (frame.outputReleased) {
                iterator.remove();
                if (frames.isEmpty()) framesByPresentationTime.remove(presentationTimeUs);
                return OptionalLong.of(frame.sequence);
            }
        }
        return OptionalLong.empty();
    }

    synchronized void clear() {
        framesByPresentationTime.clear();
    }

    private static final class FrameIdentity {
        private final long sequence;
        private boolean outputReleased;

        FrameIdentity(long sequence) {
            this.sequence = sequence;
        }
    }
}
