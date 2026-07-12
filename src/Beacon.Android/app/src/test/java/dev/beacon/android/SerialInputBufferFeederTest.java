package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;
import java.util.Queue;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicBoolean;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class SerialInputBufferFeederTest {
    @Test
    public void preservesSubmissionOrderAndRejectsWorkAfterClose() {
        Queue<Runnable> queued = new ArrayDeque<>();
        AtomicBoolean shutdown = new AtomicBoolean();
        SerialInputBufferFeeder feeder = new SerialInputBufferFeeder(
            queued::add,
            () -> shutdown.set(true));
        List<Integer> observed = new ArrayList<>();

        assertTrue(feeder.submit(() -> observed.add(1)));
        assertTrue(feeder.submit(() -> observed.add(2)));
        queued.remove().run();
        queued.remove().run();
        feeder.close();

        assertEquals(List.of(1, 2), observed);
        assertTrue(shutdown.get());
        assertFalse(feeder.submit(() -> observed.add(3)));
    }

    @Test
    public void closeDiscardsQueuedWorkAfterActiveSubmissionCompletes() throws Exception {
        Queue<Runnable> queued = new ArrayDeque<>();
        CountDownLatch closeStarted = new CountDownLatch(1);
        SerialInputBufferFeeder feeder = new SerialInputBufferFeeder(
            queued::add,
            closeStarted::countDown);
        AtomicBoolean firstRan = new AtomicBoolean();
        AtomicBoolean secondRan = new AtomicBoolean();
        assertTrue(feeder.submit(() -> firstRan.set(true)));
        assertTrue(feeder.submit(() -> secondRan.set(true)));

        Thread closer = new Thread(feeder::close, "feeder-test-close");
        closer.start();
        closeStarted.await();
        queued.remove().run();
        queued.remove().run();
        closer.join();

        assertFalse(firstRan.get());
        assertFalse(secondRan.get());
    }
}
