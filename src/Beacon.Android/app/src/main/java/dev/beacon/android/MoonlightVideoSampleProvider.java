package dev.beacon.android;

import java.util.ArrayDeque;
import java.util.Deque;
import java.util.concurrent.locks.Condition;
import java.util.concurrent.locks.ReentrantLock;

final class MoonlightVideoSampleProvider implements EncodedVideoSampleProvider {
    private static final int SampleCapacity = 8;

    private final int capacity;
    private final Deque<EncodedVideoSample> samples = new ArrayDeque<>();
    private final ReentrantLock gate = new ReentrantLock();
    private final Condition sampleAvailable = gate.newCondition();
    private final Condition capacityAvailable = gate.newCondition();
    private boolean finished;

    MoonlightVideoSampleProvider() {
        this(SampleCapacity);
    }

    MoonlightVideoSampleProvider(int capacity) {
        if (capacity <= 0) {
            throw new IllegalArgumentException("Native video sample capacity must be positive.");
        }
        this.capacity = capacity;
    }

    void submit(byte[] data, long presentationTimeUs) {
        EncodedVideoSample sample = EncodedVideoSample.data(data, presentationTimeUs);
        gate.lock();
        try {
            while (samples.size() >= capacity && !finished) {
                capacityAvailable.await();
            }
            if (finished) {
                return;
            }
            samples.addLast(sample);
            sampleAvailable.signal();
        } catch (InterruptedException ex) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("Native video sample submission was interrupted.", ex);
        } finally {
            gate.unlock();
        }
    }

    void finish() {
        gate.lock();
        try {
            finished = true;
            samples.clear();
            capacityAvailable.signalAll();
            sampleAvailable.signalAll();
        } finally {
            gate.unlock();
        }
    }

    @Override
    public EncodedVideoSample nextSample() {
        gate.lock();
        try {
            while (samples.isEmpty() && !finished) {
                sampleAvailable.await();
            }
            if (samples.isEmpty()) {
                return EncodedVideoSample.eos();
            }
            EncodedVideoSample sample = samples.removeFirst();
            capacityAvailable.signal();
            return sample;
        } catch (InterruptedException ex) {
            Thread.currentThread().interrupt();
            return EncodedVideoSample.eos();
        } finally {
            gate.unlock();
        }
    }
}
