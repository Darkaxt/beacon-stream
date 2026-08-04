package dev.beacon.android;

import java.util.ArrayList;
import java.util.List;

final class BeaconActivityEventSource implements BeaconVideoFeedbackBridge.Observer {
    enum Kind {
        PRESENCE_ACTIVE,
        PRESENCE_DEPARTED,
        BENCHMARK_COMPLETE,
        CATALOG_LOADED,
        CONNECTION_GRANTED,
        ACTION_COMPLETE,
        ACTION_FAILED,
        VIDEO_FRAME
    }

    record Event(
        long sequence,
        Kind kind,
        String operation,
        String detail,
        long streamGeneration,
        long frameSequence,
        long presentationTimeUs,
        long renderedAtUs) { }

    interface Observer {
        void onEvent(Event event);
    }

    private final List<Event> durableHistory = new ArrayList<>();
    private final List<Observer> observers = new ArrayList<>();
    private long nextSequence = 1;

    synchronized void addObserver(Observer observer) {
        if (observer == null) {
            throw new IllegalArgumentException("Beacon Activity event observer is required.");
        }
        observers.add(observer);
        for (Event event : durableHistory) observer.onEvent(event);
    }

    synchronized void removeObserver(Observer observer) {
        observers.remove(observer);
    }

    synchronized void publish(Kind kind, String operation, String detail) {
        publishLocked(kind, operation, detail, 0, 0, 0, 0, true);
    }

    @Override
    public synchronized void onRenderedFrameSent(
        long generation,
        long frameSequence,
        long presentationTimeUs,
        long renderedAtUs) {
        publishLocked(
            Kind.VIDEO_FRAME,
            "video",
            "rendered",
            generation,
            frameSequence,
            presentationTimeUs,
            renderedAtUs,
            false);
    }

    private void publishLocked(
        Kind kind,
        String operation,
        String detail,
        long streamGeneration,
        long frameSequence,
        long presentationTimeUs,
        long renderedAtUs,
        boolean durable) {
        if (kind == null) {
            throw new IllegalArgumentException("Beacon Activity event kind is required.");
        }
        Event event = new Event(
            nextSequence++,
            kind,
            operation == null ? "" : operation,
            detail == null ? "" : detail,
            streamGeneration,
            frameSequence,
            presentationTimeUs,
            renderedAtUs);
        if (durable) durableHistory.add(event);
        for (Observer observer : List.copyOf(observers)) observer.onEvent(event);
    }
}
