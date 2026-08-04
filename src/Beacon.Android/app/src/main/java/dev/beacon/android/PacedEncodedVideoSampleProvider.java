package dev.beacon.android;

import java.util.concurrent.locks.LockSupport;

final class PacedEncodedVideoSampleProvider implements EncodedVideoSampleProvider {
    private final EncodedVideoSampleProvider source;
    private final NanoClock clock;
    private final NanoWaiter waiter;
    private Long startNs;

    PacedEncodedVideoSampleProvider(EncodedVideoSampleProvider source) {
        this(source, System::nanoTime, LockSupport::parkNanos);
    }

    PacedEncodedVideoSampleProvider(
        EncodedVideoSampleProvider source,
        NanoClock clock,
        NanoWaiter waiter) {
        if (source == null || clock == null || waiter == null) {
            throw new IllegalArgumentException("Paced sample-provider dependencies are required.");
        }
        this.source = source;
        this.clock = clock;
        this.waiter = waiter;
    }

    @Override
    public synchronized EncodedVideoSample nextSample() {
        EncodedVideoSample sample = source.nextSample();
        if (sample.endOfStream()) {
            return sample;
        }

        long offsetNs = Math.multiplyExact(sample.presentationTimeUs(), 1_000L);
        if (startNs == null) {
            startNs = Math.subtractExact(clock.nanoTime(), offsetNs);
        }
        long dueNs = Math.addExact(startNs, offsetNs);
        long remainingNs;
        while ((remainingNs = dueNs - clock.nanoTime()) > 0) {
            waiter.await(remainingNs);
        }
        return sample;
    }

    @Override
    public int maxSampleBytes() {
        return source.maxSampleBytes();
    }

    @FunctionalInterface
    interface NanoClock {
        long nanoTime();
    }

    @FunctionalInterface
    interface NanoWaiter {
        void await(long durationNs);
    }
}
