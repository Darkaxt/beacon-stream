package dev.beacon.android;

import org.junit.Test;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

public final class BeaconStreamCoreTest {
    @Test
    public void callbacksUseDedicatedExecutorAndStopAfterClose() throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        ExecutorService executor = Executors.newSingleThreadExecutor(r -> new Thread(r, "beacon-frame"));
        CountDownLatch delivered = new CountDownLatch(1);
        AtomicReference<String> callbackThread = new AtomicReference<>();
        AtomicInteger frames = new AtomicInteger();
        BeaconStreamCore core = new BeaconStreamCore(bindings, frame -> {
            callbackThread.set(Thread.currentThread().getName());
            frames.incrementAndGet();
            delivered.countDown();
        }, executor);
        core.start(session("frame-callback"));

        bindings.callbacks.onFrame(new byte[] { 1, 2, 3 }, 4, 1, 1);
        delivered.await();
        assertEquals("beacon-frame", callbackThread.get());
        assertNotEquals(Thread.currentThread().getName(), callbackThread.get());

        core.close();
        bindings.callbacks.onFrame(new byte[] { 4 }, 5, 2, 1);
        assertEquals(1, frames.get());
        assertEquals(1, bindings.stopCount);
        assertEquals(1, bindings.releaseCount);
    }

    @Test
    public void replayingTheSameConsumedGrantIsIdempotent() {
        RecordingBindings bindings = new RecordingBindings();
        ExecutorService executor = Executors.newSingleThreadExecutor();
        BeaconStreamCore core = new BeaconStreamCore(bindings, frame -> { }, executor);
        BeaconStreamSession session = BeaconStreamSession.parse(
            "https://beacon.example",
            "client",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":1,\"planExplanation\":\"selected\",\"sessionId\":\"s\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"h264\",\"width\":1280,\"height\":720,\"framesPerSecondNumerator\":60,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"sdr\"}}}");

        core.start(session);
        core.start(session);
        assertTrue(session.ticketConsumed());
        assertEquals(3, bindings.ticketLength);
        assertEquals(1, bindings.startCount);
        assertTrue(allZero(bindings.ticketReference));
        core.stop();
        core.stop();
        core.close();
        core.close();
        assertEquals(1, bindings.stopCount);
        assertEquals(1, bindings.releaseCount);
        assertFalse(core.isOpen());
    }

    @Test
    public void freshSameSessionReconnectStopsAndHandsOffReplacementTicket() {
        RecordingBindings bindings = new RecordingBindings();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());
        BeaconStreamSession original = session("same", "AQID");
        BeaconStreamSession replacement = session("same", "BAUG");

        core.start(original);
        core.start(replacement);

        assertEquals(2, bindings.startCount);
        assertEquals(1, bindings.stopCount);
        assertEquals(Arrays.asList(
            Arrays.asList((byte) 1, (byte) 2, (byte) 3),
            Arrays.asList((byte) 4, (byte) 5, (byte) 6)), bindings.ticketSnapshots);
        assertTrue(allZero(bindings.ticketReferences.get(0)));
        assertTrue(allZero(bindings.ticketReferences.get(1)));
        assertTrue(original.ticketConsumed());
        assertTrue(replacement.ticketConsumed());
        core.close();
    }

    @Test
    public void freshSameSessionReconnectWipesReplacementTicketWhenNativeStartFails() {
        RecordingBindings bindings = new RecordingBindings();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());
        BeaconStreamSession original = session("same", "AQID");
        BeaconStreamSession replacement = session("same", "BAUG");
        core.start(original);
        bindings.failStart = true;

        try {
            core.start(replacement);
            throw new AssertionError("Expected replacement native start failure.");
        } catch (IllegalStateException expected) {
            assertEquals("native start failed", expected.getMessage());
        }

        assertEquals(2, bindings.startCount);
        assertEquals(1, bindings.stopCount);
        assertEquals(Arrays.asList(
            Arrays.asList((byte) 1, (byte) 2, (byte) 3),
            Arrays.asList((byte) 4, (byte) 5, (byte) 6)), bindings.ticketSnapshots);
        assertTrue(allZero(bindings.ticketReferences.get(0)));
        assertTrue(allZero(bindings.ticketReferences.get(1)));
        assertTrue(original.ticketConsumed());
        assertTrue(replacement.ticketConsumed());
        core.close();
    }

    @Test
    public void transportShutdownPendingReconnectAcceptsTicketAndIgnoresOldLoss() {
        RecordingBindings bindings = new RecordingBindings();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());
        BeaconStreamSession original = session("pending", "AQID");
        BeaconStreamSession replacement = session("pending", "BAUG");

        core.start(original);
        core.start(replacement);
        bindings.callbacks.onConnectionLost(1);
        core.stop();

        assertEquals(Arrays.asList(1L, 2L), bindings.startGenerations);
        assertEquals(2, bindings.startCount);
        assertEquals(2, bindings.stopCount);
        assertEquals(0, core.connectionLossCountForTest());
        assertTrue(allZero(bindings.ticketReferences.get(0)));
        assertTrue(allZero(bindings.ticketReferences.get(1)));
        core.close();
    }

    @Test
    public void explicitStopReconnectIgnoresDeferredOldLoss() {
        RecordingBindings bindings = new RecordingBindings();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());

        core.start(session("explicit", "AQID"));
        core.stop();
        core.start(session("explicit", "BAUG"));
        bindings.callbacks.onConnectionLost(1);
        core.stop();

        assertEquals(Arrays.asList(1L, 2L), bindings.startGenerations);
        assertEquals(2, bindings.stopCount);
        assertEquals(0, core.connectionLossCountForTest());
        core.close();
    }

    @Test
    public void currentGenerationReceivesExactlyOneLoss() {
        RecordingBindings bindings = new RecordingBindings();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());
        core.start(session("current"));

        bindings.callbacks.onConnectionLost(1);
        bindings.callbacks.onConnectionLost(1);

        assertEquals(1, core.connectionLossCountForTest());
        assertTrue(core.stoppedForTest());
        core.stop();
        assertEquals(0, bindings.stopCount);
        core.close();
    }

    @Test
    public void acceptedConnectionLossReportsFixedFailureStageExactlyOnce() {
        RecordingBindings bindings = new RecordingBindings();
        AtomicReference<String> failureStage = new AtomicReference<>();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> { },
            Executors.newSingleThreadExecutor(),
            () -> { },
            failureStage::set);
        core.start(session("observed-loss"));

        bindings.callbacks.onConnectionLost(2);
        assertNull(failureStage.get());
        bindings.callbacks.onConnectionLost(1);
        bindings.callbacks.onConnectionLost(1);

        assertEquals("transport", failureStage.get());
        core.close();
    }

    @Test
    public void acceptedConnectionLossReleasesEventDrivenWaiter() throws InterruptedException {
        RecordingBindings bindings = new RecordingBindings();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());
        core.start(session("await-loss"));

        bindings.callbacks.onConnectionLost(1);
        core.awaitConnectionLossForTest(1);

        assertEquals(1, core.connectionLossCountForTest());
        assertTrue(core.stoppedForTest());
        core.close();
    }

    @Test
    public void lossDeliveredInsideAcceptedStartCommitsStoppedState() {
        RecordingBindings bindings = new RecordingBindings();
        bindings.reportLossInsideStart = true;
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());

        core.start(session("inline-loss"));

        assertTrue(core.stoppedForTest());
        assertEquals(1, core.connectionLossCountForTest());
        assertEquals(1, core.activeGenerationForTest());
        core.close();
    }

    @Test
    public void rejectedReplacementWipesTicketAndReportsStoppedState() {
        RecordingBindings bindings = new RecordingBindings();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());
        BeaconStreamSession original = session("rejected", "AQID");
        BeaconStreamSession replacement = session("rejected", "BAUG");
        core.start(original);
        bindings.acceptStart = false;

        try {
            core.start(replacement);
            throw new AssertionError("Expected rejected pending reconnect.");
        } catch (IllegalStateException expected) {
            assertEquals("Native StreamCore rejected generation 2.", expected.getMessage());
        }

        assertEquals(Arrays.asList(1L, 2L), bindings.startGenerations);
        assertTrue(original.ticketConsumed());
        assertTrue(replacement.ticketConsumed());
        assertTrue(allZero(bindings.ticketReferences.get(0)));
        assertTrue(allZero(bindings.ticketReferences.get(1)));
        assertTrue(core.stoppedForTest());
        assertEquals(0, core.activeGenerationForTest());
        bindings.callbacks.onConnectionLost(1);
        assertEquals(0, core.connectionLossCountForTest());
        core.close();
    }

    @Test
    public void ticketIsZeroedWhenNativeStartFails() {
        RecordingBindings bindings = new RecordingBindings();
        bindings.failStart = true;
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());
        try {
            core.start(session("s-failure"));
            throw new AssertionError("Expected native start failure.");
        } catch (IllegalStateException expected) {
            assertEquals("native start failed", expected.getMessage());
        }
        assertTrue(allZero(bindings.ticketReference));
        core.close();
    }

    @Test
    public void closeDrainsInflightCallbackAndDiscardsQueuedCallback() throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        CountDownLatch sinkEntered = new CountDownLatch(1);
        CountDownLatch releaseSink = new CountDownLatch(1);
        CountDownLatch closeStarted = new CountDownLatch(1);
        CountDownLatch closeFinished = new CountDownLatch(1);
        AtomicInteger frames = new AtomicInteger();
        bindings.stopCalled = new CountDownLatch(1);
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> {
                frames.incrementAndGet();
                sinkEntered.countDown();
                try {
                    releaseSink.await();
                } catch (InterruptedException error) {
                    Thread.currentThread().interrupt();
                    throw new AssertionError(error);
                }
            },
            Executors.newSingleThreadExecutor());
        core.start(session("close-drain"));
        bindings.callbacks.onFrame(new byte[] { 1 }, 1, 1, 1);
        sinkEntered.await();
        bindings.callbacks.onFrame(new byte[] { 2 }, 2, 2, 1);
        Thread closer = new Thread(() -> {
            closeStarted.countDown();
            core.close();
            closeFinished.countDown();
        });
        closer.start();
        closeStarted.await();
        bindings.stopCalled.await();
        assertEquals(0, bindings.releaseCount);
        releaseSink.countDown();
        closeFinished.await();
        closer.join();
        assertEquals(1, frames.get());
        assertEquals(1, bindings.releaseCount);
    }

    @Test
    public void fakeSinkCompletionEmitsTypedQueueDepthFeedbackAfterFrame() throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        CountDownLatch feedbackSent = new CountDownLatch(1);
        bindings.feedbackSent = feedbackSent;
        AtomicInteger order = new AtomicInteger();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> assertEquals(1, order.incrementAndGet()),
            Executors.newSingleThreadExecutor());
        core.start(session("feedback"));
        bindings.feedbackOrder = order;
        bindings.callbacks.onFrame(new byte[] { 1 }, 1, 1, 1);
        feedbackSent.await();
        assertEquals(0, bindings.lastQueuedAccessUnits);
        assertEquals(0, bindings.lastDroppedAccessUnits);
        assertEquals(2, order.get());
        core.close();
    }

    @Test
    public void feedbackObserverRunsAfterSuccessfulNativeFeedbackHandoff() throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        CountDownLatch observerCalled = new CountDownLatch(1);
        AtomicInteger order = new AtomicInteger();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> assertEquals(1, order.incrementAndGet()),
            Executors.newSingleThreadExecutor(),
            () -> {
                assertEquals(3, order.incrementAndGet());
                observerCalled.countDown();
            });
        bindings.feedbackOrder = order;
        core.start(session("feedback-observer"));

        bindings.callbacks.onFrame(new byte[] { 1 }, 1, 1, 1);
        observerCalled.await();

        assertEquals(3, order.get());
        core.close();
    }

    @Test
    public void failedNativeFeedbackReportsFailureBeforeExecutorTerminates() throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        bindings.failFeedback = true;
        CountDownLatch failureObserved = new CountDownLatch(1);
        AtomicReference<String> failureStage = new AtomicReference<>();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> { },
            Executors.newSingleThreadExecutor(),
            () -> { },
            stage -> {
                failureStage.set(stage);
                failureObserved.countDown();
            });
        core.start(session("feedback-failure"));

        bindings.callbacks.onFrame(new byte[] { 1 }, 1, 1, 1);
        failureObserved.await();

        assertEquals("feedback", failureStage.get());
        core.close();
    }

    @Test
    public void oldFrameCompletionCannotSendFeedbackIntoReplacementGeneration()
        throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        CountDownLatch oldSinkEntered = new CountDownLatch(1);
        CountDownLatch releaseOldSink = new CountDownLatch(1);
        CountDownLatch replacementFeedback = new CountDownLatch(1);
        bindings.feedbackSentForGeneration2 = replacementFeedback;
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> {
                if (frame.presentationTimeUs != 1) return;
                oldSinkEntered.countDown();
                try {
                    releaseOldSink.await();
                } catch (InterruptedException error) {
                    Thread.currentThread().interrupt();
                    throw new AssertionError(error);
                }
            },
            Executors.newSingleThreadExecutor());

        core.start(session("feedback-race", "AQID"));
        bindings.callbacks.onFrame(new byte[] { 1 }, 1, 1, 1);
        oldSinkEntered.await();
        core.start(session("feedback-race", "BAUG"));
        releaseOldSink.countDown();
        bindings.callbacks.onFrame(new byte[] { 2 }, 2, 2, 2);
        replacementFeedback.await();

        assertEquals(Arrays.asList(2L), bindings.feedbackGenerations);
        core.close();
    }

    @Test
    public void explicitStopWhileSinkBlockedPreventsPostSinkFeedbackAndExecutorFailure()
        throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        CountDownLatch sinkEntered = new CountDownLatch(1);
        CountDownLatch releaseSink = new CountDownLatch(1);
        CountDownLatch stopFinished = new CountDownLatch(1);
        CountDownLatch executorDrained = new CountDownLatch(1);
        AtomicReference<Throwable> executorFailure = new AtomicReference<>();
        ExecutorService executor = Executors.newSingleThreadExecutor(r -> {
            Thread thread = new Thread(r, "beacon-stop-blocked-sink");
            thread.setUncaughtExceptionHandler((ignored, error) -> executorFailure.set(error));
            return thread;
        });
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> {
                sinkEntered.countDown();
                try {
                    releaseSink.await();
                } catch (InterruptedException error) {
                    Thread.currentThread().interrupt();
                    throw new AssertionError(error);
                }
            },
            executor);
        core.start(session("stop-blocked-sink"));
        bindings.callbacks.onFrame(new byte[] { 1 }, 1, 1, 1);
        sinkEntered.await();

        Thread stopper = new Thread(() -> {
            core.stop();
            stopFinished.countDown();
        });
        stopper.start();
        stopFinished.await();
        releaseSink.countDown();
        executor.execute(executorDrained::countDown);
        executorDrained.await();
        stopper.join();

        assertTrue(bindings.feedbackGenerations.isEmpty());
        assertNull(executorFailure.get());
        core.close();
    }

    @Test
    public void currentLossWhileSinkBlockedPreventsPostSinkFeedbackAndExecutorFailure()
        throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        CountDownLatch sinkEntered = new CountDownLatch(1);
        CountDownLatch releaseSink = new CountDownLatch(1);
        CountDownLatch executorDrained = new CountDownLatch(1);
        AtomicReference<Throwable> executorFailure = new AtomicReference<>();
        ExecutorService executor = Executors.newSingleThreadExecutor(r -> {
            Thread thread = new Thread(r, "beacon-loss-blocked-sink");
            thread.setUncaughtExceptionHandler((ignored, error) -> executorFailure.set(error));
            return thread;
        });
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> {
                sinkEntered.countDown();
                try {
                    releaseSink.await();
                } catch (InterruptedException error) {
                    Thread.currentThread().interrupt();
                    throw new AssertionError(error);
                }
            },
            executor);
        core.start(session("loss-blocked-sink"));
        bindings.callbacks.onFrame(new byte[] { 1 }, 1, 1, 1);
        sinkEntered.await();

        bindings.callbacks.onConnectionLost(1);
        assertTrue(core.stoppedForTest());
        releaseSink.countDown();
        executor.execute(executorDrained::countDown);
        executorDrained.await();

        assertTrue(bindings.feedbackGenerations.isEmpty());
        assertNull(executorFailure.get());
        core.close();
    }

    @Test
    public void bindingsCallsNeverRunWhileHoldingTheCoreMonitor() throws Exception {
        MonitorCheckingBindings bindings = new MonitorCheckingBindings();
        CountDownLatch feedbackSent = new CountDownLatch(1);
        bindings.feedbackSent = feedbackSent;
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());
        bindings.core = core;

        core.start(session("monitor"));
        core.sendInput(BeaconApiClient.InputBatch.pointerTap(1, 0.5, 0.5));
        core.replaceSurface(new Object());
        bindings.callbacks.onFrame(new byte[] { 1 }, 1, 1, 1);
        feedbackSent.await();
        Thread stopper = new Thread(core::stop);
        stopper.start();
        stopper.join();
        Thread closer = new Thread(core::close);
        closer.start();
        closer.join();

        assertFalse(bindings.monitorHeldAcrossBinding);
    }

    private static BeaconStreamSession session(String sessionId) {
        return session(sessionId, "AQID");
    }

    private static BeaconStreamSession session(String sessionId, String ticket) {
        return BeaconStreamSession.parse(
            "https://beacon.example",
            "client",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"" + ticket + "\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":1,\"planExplanation\":\"selected\",\"sessionId\":\"" + sessionId + "\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"h264\",\"width\":1280,\"height\":720,\"framesPerSecondNumerator\":60,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"sdr\"}}}");
    }

    private static boolean allZero(byte[] bytes) {
        for (byte value : bytes) {
            if (value != 0) return false;
        }
        return true;
    }

    private static final class RecordingBindings implements BeaconStreamCore.Bindings {
        BeaconStreamCore.NativeCallbacks callbacks;
        int stopCount;
        int releaseCount;
        int ticketLength;
        int startCount;
        byte[] ticketReference;
        final List<byte[]> ticketReferences = new ArrayList<>();
        final List<List<Byte>> ticketSnapshots = new ArrayList<>();
        final List<Long> startGenerations = new ArrayList<>();
        boolean failStart;
        boolean acceptStart = true;
        boolean reportLossInsideStart;
        boolean failFeedback;
        CountDownLatch feedbackSent;
        CountDownLatch feedbackSentForGeneration2;
        CountDownLatch stopCalled;
        AtomicInteger feedbackOrder;
        final List<Long> feedbackGenerations = new ArrayList<>();
        int lastQueuedAccessUnits = -1;
        long lastDroppedAccessUnits = -1;

        @Override public long create(BeaconStreamCore.NativeCallbacks value) { callbacks = value; return 7; }
        @Override public boolean start(long handle, BeaconStreamSession.NativeGrant grant) {
            startCount++;
            startGenerations.add(grant.generation);
            ticketLength = grant.ticket.length;
            ticketReference = grant.ticket;
            ticketReferences.add(grant.ticket);
            List<Byte> snapshot = new ArrayList<>();
            for (byte value : grant.ticket) snapshot.add(value);
            ticketSnapshots.add(snapshot);
            if (failStart) throw new IllegalStateException("native start failed");
            if (reportLossInsideStart) callbacks.onConnectionLost(grant.generation);
            return acceptStart;
        }
        @Override public void sendInput(long handle, BeaconApiClient.InputBatch input) { }
        @Override public void sendQueueDepthFeedback(
            long handle, long generation, int queuedAccessUnits,
            long droppedAccessUnits) {
            if (failFeedback) throw new IllegalStateException("native feedback failed");
            feedbackGenerations.add(generation);
            lastQueuedAccessUnits = queuedAccessUnits;
            lastDroppedAccessUnits = droppedAccessUnits;
            if (feedbackOrder != null) feedbackOrder.incrementAndGet();
            if (feedbackSent != null) feedbackSent.countDown();
            if (generation == 2 && feedbackSentForGeneration2 != null) {
                feedbackSentForGeneration2.countDown();
            }
        }
        @Override public void replaceSurface(long handle, Object surface) { }
        @Override public void stop(long handle) {
            stopCount++;
            if (stopCalled != null) stopCalled.countDown();
        }
        @Override public void release(long handle) { releaseCount++; }
    }

    private static final class MonitorCheckingBindings implements BeaconStreamCore.Bindings {
        BeaconStreamCore core;
        BeaconStreamCore.NativeCallbacks callbacks;
        CountDownLatch feedbackSent;
        volatile boolean monitorHeldAcrossBinding;

        private void recordMonitor() {
            monitorHeldAcrossBinding |= Thread.holdsLock(core);
        }

        @Override public long create(BeaconStreamCore.NativeCallbacks value) {
            callbacks = value;
            return 8;
        }
        @Override public boolean start(long handle, BeaconStreamSession.NativeGrant grant) {
            recordMonitor();
            return true;
        }
        @Override public void sendInput(long handle, BeaconApiClient.InputBatch input) {
            recordMonitor();
        }
        @Override public void sendQueueDepthFeedback(
            long handle, long generation, int queuedAccessUnits,
            long droppedAccessUnits) {
            recordMonitor();
            feedbackSent.countDown();
        }
        @Override public void replaceSurface(long handle, Object surface) {
            recordMonitor();
        }
        @Override public void stop(long handle) { recordMonitor(); }
        @Override public void release(long handle) { recordMonitor(); }
    }
}
