package dev.beacon.android;

import org.junit.Test;

import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class BeaconAccessUnitQueueTest {
    @Test
    public void waitsForCodecConfigurationAndIdrInTheSameAccessUnit() {
        RecordingObserver observer = new RecordingObserver();
        BeaconAccessUnitQueue queue = new BeaconAccessUnitQueue(3, observer);

        queue.onFrame(frame(1, false, false));
        queue.onFrame(frame(2, true, false));
        queue.onFrame(frame(3, false, true));

        assertEquals(new QueueState(0, 3), observer.lastState());
        assertEquals(List.of(1L), observer.idrRequests);

        queue.onFrame(frame(4, true, true));
        EncodedVideoSample sample = queue.nextSample();

        assertFalse(sample.endOfStream());
        assertArrayEquals(new byte[] { 4 }, sample.data());
        assertEquals(4, sample.sequence());
        assertEquals(4_000, sample.presentationTimeUs());
        assertTrue(sample.idr());
        assertTrue(sample.codecConfiguration());
        assertEquals(List.of(false), observer.awaitingIdrChanges);
        assertEquals(new QueueState(0, 3), observer.lastState());
        queue.close();
    }

    @Test
    public void overflowDropsStaleQueueAndRecoversOnlyOnFreshConfiguredIdr() {
        RecordingObserver observer = new RecordingObserver();
        BeaconAccessUnitQueue queue = new BeaconAccessUnitQueue(2, observer);
        queue.onFrame(frame(10, true, true));
        queue.onFrame(frame(11, false, false));

        queue.onFrame(frame(12, false, false));

        assertEquals(new QueueState(0, 3), observer.lastState());
        assertEquals(List.of(12L), observer.idrRequests);

        queue.onFrame(frame(13, true, false));
        assertEquals(new QueueState(0, 4), observer.lastState());
        assertEquals(List.of(12L), observer.idrRequests);

        queue.onFrame(frame(14, true, true));
        EncodedVideoSample recovered = queue.nextSample();

        assertEquals(14, recovered.sequence());
        assertEquals(List.of(false, true, false), observer.awaitingIdrChanges);
        assertEquals(new QueueState(0, 4), observer.lastState());
        queue.close();
    }

    @Test
    public void decoderResetDropsQueuedUnitsAndRequestsOneFreshIdr() {
        RecordingObserver observer = new RecordingObserver();
        BeaconAccessUnitQueue queue = new BeaconAccessUnitQueue(3, observer);
        queue.onFrame(frame(20, true, true));
        queue.onFrame(frame(21, false, false));

        queue.resetForDecoder();
        queue.resetForDecoder();

        assertEquals(new QueueState(0, 2), observer.lastState());
        assertEquals(List.of(21L), observer.idrRequests);
        assertEquals(List.of(false, true), observer.awaitingIdrChanges);
        queue.onFrame(frame(22, false, false));
        assertEquals(new QueueState(0, 3), observer.lastState());
        assertEquals(List.of(21L), observer.idrRequests);
        queue.close();
    }

    @Test
    public void closeUnblocksWaitingConsumerWithEndOfStreamAndIsIdempotent()
        throws Exception {
        RecordingObserver observer = new RecordingObserver();
        BeaconAccessUnitQueue queue = new BeaconAccessUnitQueue(2, observer);
        CountDownLatch consumerStarted = new CountDownLatch(1);
        CountDownLatch consumerFinished = new CountDownLatch(1);
        AtomicReference<EncodedVideoSample> observed = new AtomicReference<>();
        Thread consumer = new Thread(() -> {
            consumerStarted.countDown();
            observed.set(queue.nextSample());
            consumerFinished.countDown();
        }, "beacon-access-unit-consumer");
        consumer.start();
        consumerStarted.await();

        queue.close();
        queue.close();
        consumerFinished.await();
        consumer.join();

        assertTrue(observed.get().endOfStream());
        assertEquals(new QueueState(0, 0), observer.lastState());
    }

    @Test
    public void decoderResetUnblocksOnlyTheOldConsumerWithEndOfStream()
        throws Exception {
        BeaconAccessUnitQueue queue = new BeaconAccessUnitQueue(
            2, BeaconAccessUnitQueue.Observer.noOp());
        CountDownLatch oldConsumerStarted = new CountDownLatch(1);
        CountDownLatch oldConsumerFinished = new CountDownLatch(1);
        AtomicReference<EncodedVideoSample> oldSample = new AtomicReference<>();
        Thread oldConsumer = new Thread(() -> {
            oldConsumerStarted.countDown();
            oldSample.set(queue.nextSample());
            oldConsumerFinished.countDown();
        }, "beacon-old-decoder-consumer");
        oldConsumer.start();
        oldConsumerStarted.await();

        queue.resetForDecoder();
        oldConsumerFinished.await();
        oldConsumer.join();
        queue.onFrame(frame(30, true, true));
        EncodedVideoSample replacementSample = queue.nextSample();

        assertTrue(oldSample.get().endOfStream());
        assertEquals(30, replacementSample.sequence());
        queue.close();
    }

    private static BeaconStreamCore.EncodedFrame frame(
        long sequence,
        boolean idr,
        boolean codecConfiguration) {
        return new BeaconStreamCore.EncodedFrame(
            directBuffer((int) sequence),
            sequence * 1_000,
            sequence,
            idr,
            codecConfiguration);
    }

    private static ByteBuffer directBuffer(int... values) {
        ByteBuffer buffer = ByteBuffer.allocateDirect(values.length);
        for (int value : values) buffer.put((byte) value);
        buffer.flip();
        return buffer;
    }

    private record QueueState(int queued, long dropped) { }

    private static final class RecordingObserver implements BeaconAccessUnitQueue.Observer {
        private final List<QueueState> states = new ArrayList<>();
        private final List<Long> idrRequests = new ArrayList<>();
        private final List<Boolean> awaitingIdrChanges = new ArrayList<>();

        @Override
        public void onQueueDepthChanged(int queuedAccessUnits, long droppedAccessUnits) {
            states.add(new QueueState(queuedAccessUnits, droppedAccessUnits));
        }

        @Override
        public void onIdrRequired(long lastCompleteSequence) {
            idrRequests.add(lastCompleteSequence);
        }

        @Override
        public void onAwaitingIdrChanged(boolean awaitingIdr) {
            awaitingIdrChanges.add(awaitingIdr);
        }

        QueueState lastState() {
            return states.get(states.size() - 1);
        }
    }
}
