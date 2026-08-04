package dev.beacon.android;

import java.util.ArrayDeque;
import java.util.Queue;

final class BeaconAccessUnitQueue
    implements EncodedVideoSampleProvider, BeaconStreamCore.EncodedFrameSink, AutoCloseable {
    interface Observer {
        void onQueueDepthChanged(int queuedAccessUnits, long droppedAccessUnits);
        void onIdrRequired(long lastCompleteSequence);
        void onAwaitingIdrChanged(boolean awaitingIdr);

        static Observer noOp() {
            return new Observer() {
                @Override public void onQueueDepthChanged(
                    int queuedAccessUnits,
                    long droppedAccessUnits) { }
                @Override public void onIdrRequired(long lastCompleteSequence) { }
                @Override public void onAwaitingIdrChanged(boolean awaitingIdr) { }
            };
        }
    }

    private final Object gate = new Object();
    private final int capacity;
    private final Observer observer;
    private final Queue<EncodedVideoSample> samples = new ArrayDeque<>();
    private long droppedAccessUnits;
    private long lastCompleteSequence;
    private boolean awaitingConfiguredIdr = true;
    private boolean idrRequested;
    private boolean closed;
    private long decoderEpoch;

    BeaconAccessUnitQueue(int capacity, Observer observer) {
        if (capacity <= 0) {
            throw new IllegalArgumentException("Access-unit queue capacity must be positive.");
        }
        if (observer == null) {
            throw new IllegalArgumentException("Access-unit queue observer is required.");
        }
        this.capacity = capacity;
        this.observer = observer;
    }

    @Override
    public void onFrame(BeaconStreamCore.EncodedFrame frame) {
        if (frame == null) {
            throw new IllegalArgumentException("Encoded frame is required.");
        }

        Notification notification;
        synchronized (gate) {
            if (closed) return;
            lastCompleteSequence = frame.sequence;
            boolean wasAwaitingConfiguredIdr = awaitingConfiguredIdr;
            boolean requestIdr = false;
            boolean configuredIdr = frame.idr && frame.codecConfiguration;
            if (awaitingConfiguredIdr) {
                if (configuredIdr) {
                    enqueue(frame);
                    awaitingConfiguredIdr = false;
                    idrRequested = false;
                } else {
                    droppedAccessUnits++;
                    requestIdr = requireIdr();
                }
            } else if (samples.size() == capacity) {
                droppedAccessUnits += samples.size();
                samples.clear();
                awaitingConfiguredIdr = true;
                if (configuredIdr) {
                    enqueue(frame);
                    awaitingConfiguredIdr = false;
                    idrRequested = false;
                } else {
                    droppedAccessUnits++;
                    requestIdr = requireIdr();
                }
            } else {
                enqueue(frame);
            }
            notification = notification(
                requestIdr,
                wasAwaitingConfiguredIdr == awaitingConfiguredIdr
                    ? null
                    : awaitingConfiguredIdr);
        }
        notification.publish(observer);
    }

    @Override
    public EncodedVideoSample nextSample() {
        Notification notification;
        EncodedVideoSample sample;
        synchronized (gate) {
            long expectedDecoderEpoch = decoderEpoch;
            while (samples.isEmpty() && !closed &&
                expectedDecoderEpoch == decoderEpoch) {
                try {
                    gate.wait();
                } catch (InterruptedException interrupted) {
                    Thread.currentThread().interrupt();
                    return EncodedVideoSample.eos();
                }
            }
            if (closed || expectedDecoderEpoch != decoderEpoch) {
                return EncodedVideoSample.eos();
            }
            sample = samples.remove();
            notification = notification(false, null);
        }
        notification.publish(observer);
        return sample;
    }

    void resetForDecoder() {
        Notification notification;
        synchronized (gate) {
            if (closed) return;
            boolean wasAwaitingConfiguredIdr = awaitingConfiguredIdr;
            droppedAccessUnits += samples.size();
            samples.clear();
            decoderEpoch++;
            gate.notifyAll();
            awaitingConfiguredIdr = true;
            notification = notification(
                requireIdr(),
                wasAwaitingConfiguredIdr ? null : true);
        }
        notification.publish(observer);
    }

    @Override
    public void close() {
        Notification notification;
        synchronized (gate) {
            if (closed) return;
            closed = true;
            samples.clear();
            gate.notifyAll();
            notification = notification(false, null);
        }
        notification.publish(observer);
    }

    private void enqueue(BeaconStreamCore.EncodedFrame frame) {
        samples.add(EncodedVideoSample.data(
            frame.bytes,
            frame.presentationTimeUs,
            frame.sequence,
            frame.idr,
            frame.codecConfiguration));
        gate.notifyAll();
    }

    private boolean requireIdr() {
        if (idrRequested) return false;
        idrRequested = true;
        return true;
    }

    private Notification notification(Boolean requestIdr, Boolean awaitingIdr) {
        return new Notification(
            samples.size(),
            droppedAccessUnits,
            requestIdr != null && requestIdr,
            awaitingIdr,
            lastCompleteSequence);
    }

    private static final class Notification {
        private final int queuedAccessUnits;
        private final long droppedAccessUnits;
        private final boolean requestIdr;
        private final Boolean awaitingIdr;
        private final long lastCompleteSequence;

        Notification(
            int queuedAccessUnits,
            long droppedAccessUnits,
            boolean requestIdr,
            Boolean awaitingIdr,
            long lastCompleteSequence) {
            this.queuedAccessUnits = queuedAccessUnits;
            this.droppedAccessUnits = droppedAccessUnits;
            this.requestIdr = requestIdr;
            this.awaitingIdr = awaitingIdr;
            this.lastCompleteSequence = lastCompleteSequence;
        }

        void publish(Observer observer) {
            observer.onQueueDepthChanged(queuedAccessUnits, droppedAccessUnits);
            if (awaitingIdr != null) observer.onAwaitingIdrChanged(awaitingIdr);
            if (requestIdr) observer.onIdrRequired(lastCompleteSequence);
        }
    }
}
