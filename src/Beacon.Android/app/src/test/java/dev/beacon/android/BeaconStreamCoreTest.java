package dev.beacon.android;

import org.junit.Test;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

public final class BeaconStreamCoreTest {
    @Test
    public void reportsStreamingOnlyBetweenAcceptedStartAndStop() {
        RecordingBindings bindings = new RecordingBindings();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> { },
            Executors.newSingleThreadExecutor());

        assertFalse(core.isStreaming());
        core.start(session("active-state"));
        assertTrue(core.isStreaming());
        core.stop();
        assertFalse(core.isStreaming());
        core.close();
        assertFalse(core.isStreaming());
    }

    @Test
    public void completeAccessUnitMetadataReachesFrameSink() throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        ExecutorService executor = Executors.newSingleThreadExecutor();
        CountDownLatch delivered = new CountDownLatch(1);
        AtomicReference<BeaconStreamCore.EncodedFrame> observed = new AtomicReference<>();
        BeaconStreamCore core = new BeaconStreamCore(bindings, frame -> {
            observed.set(frame);
            delivered.countDown();
        }, executor);
        core.start(session("complete-access-unit"));

        bindings.callbacks.onFrame(
            directBuffer(0, 0, 0, 1, 0x67),
            2_345_678,
            77,
            1,
            true,
            true);

        delivered.await();
        BeaconStreamCore.EncodedFrame frame = observed.get();
        assertNotNull(frame);
        assertEquals(2_345_678, frame.presentationTimeUs);
        assertEquals(77, frame.sequence);
        assertTrue(frame.idr);
        assertTrue(frame.codecConfiguration);
        assertTrue(frame.bytes.isDirect());
        assertTrue(frame.bytes.isReadOnly());
        assertArrayEquals(new byte[] { 0, 0, 0, 1, 0x67 }, bytes(frame.bytes));
        core.close();
    }

    @Test
    public void decodedAudioPcmUsesTheGenerationScopedCallbackExecutor() throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        ExecutorService executor = Executors.newSingleThreadExecutor(
            action -> new Thread(action, "beacon-media-callback"));
        CountDownLatch delivered = new CountDownLatch(1);
        AtomicReference<String> callbackThread = new AtomicReference<>();
        AtomicReference<BeaconStreamCore.DecodedAudioFrame> observed =
            new AtomicReference<>();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> { },
            frame -> {
                callbackThread.set(Thread.currentThread().getName());
                observed.set(frame);
                delivered.countDown();
            },
            executor);
        core.start(session("audio-pcm"));
        ByteBuffer pcm = ByteBuffer.allocateDirect(1_920 * Float.BYTES)
            .order(ByteOrder.nativeOrder());
        pcm.putFloat(0.25F);
        pcm.position(0);

        bindings.callbacks.onAudioPcm(pcm, 20_000, 1, 1, false);

        delivered.await();
        BeaconStreamCore.DecodedAudioFrame frame = observed.get();
        assertNotNull(frame);
        assertEquals("beacon-media-callback", callbackThread.get());
        assertEquals(20_000, frame.presentationTimeUs);
        assertEquals(1, frame.sequence);
        assertFalse(frame.concealed);
        assertTrue(frame.pcm.isDirect());
        assertTrue(frame.pcm.isReadOnly());
        assertEquals(1_920 * Float.BYTES, frame.pcm.remaining());
        assertEquals(0.25F, frame.pcm.order(ByteOrder.nativeOrder()).getFloat(), 0.0001F);
        core.close();
    }

    @Test
    public void benchmarkCompletionUsesCallbackExecutorAndPreservesPacketEvidence() throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        ExecutorService executor = Executors.newSingleThreadExecutor(r -> new Thread(r, "beacon-benchmark"));
        CountDownLatch delivered = new CountDownLatch(1);
        AtomicReference<String> callbackThread = new AtomicReference<>();
        AtomicReference<BeaconStreamCore.BenchmarkNetworkResult> observed = new AtomicReference<>();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> { },
            executor,
            () -> { },
            stage -> { },
            result -> {
                callbackThread.set(Thread.currentThread().getName());
                observed.set(result);
                delivered.countDown();
            });
        core.start(benchmarkSession());

        bindings.callbacks.onBenchmarkCompleted(
            96.5,
            new long[] { 0, 1 },
            new int[] { 1000, 1000 },
            new long[] { 2000, 2500 },
            new long[] { 0, 300 },
            new int[] { 0, 1 },
            new boolean[] { true, false },
            1);

        delivered.await();
        BeaconStreamCore.BenchmarkNetworkResult result = observed.get();
        assertNotNull(result);
        assertEquals("beacon-benchmark", callbackThread.get());
        assertEquals(96.5, result.sustainableThroughputMbps, 0.001);
        assertEquals(2, result.samples.size());
        assertEquals(2500, result.samples.get(1).rttUs);
        assertEquals(1, result.samples.get(1).reorderDistance);
        assertFalse(result.samples.get(1).received);
        core.close();
    }

    @Test
    public void frameSinkUsesCallbackExecutorAndStopsAfterClose() throws Exception {
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

        bindings.callbacks.onFrame(directBuffer(1, 2, 3), 4, 1, 1, false, false);
        delivered.await();
        assertEquals("beacon-frame", callbackThread.get());
        assertEquals(1, frames.get());

        core.close();
        bindings.callbacks.onFrame(directBuffer(4), 5, 2, 1, false, false);
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
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":1,\"planExplanation\":\"selected\",\"sessionId\":\"s\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"h264\",\"width\":1280,\"height\":720,\"framesPerSecondNumerator\":60,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"sdr\"},\"selectedAudio\":{\"codec\":\"opus\",\"sampleRateHz\":48000,\"channelCount\":2,\"frameDurationUs\":20000,\"bitrateBps\":96000}}}");

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
    public void explicitStopIgnoresCurrentGenerationLoss() {
        RecordingBindings bindings = new RecordingBindings();
        AtomicReference<String> failureStage = new AtomicReference<>();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> { },
            Executors.newSingleThreadExecutor(),
            () -> { },
            failureStage::set);

        core.start(session("explicit-current"));
        core.stop();
        bindings.callbacks.onConnectionLost(1);

        assertEquals(0, core.connectionLossCountForTest());
        assertNull(failureStage.get());
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
    public void closeFromFrameSinkIsRejectedWithoutReleasingTheCore() throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        AtomicReference<BeaconStreamCore> coreReference = new AtomicReference<>();
        AtomicReference<Throwable> failure = new AtomicReference<>();
        CountDownLatch callbackCompleted = new CountDownLatch(1);
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> {
                try {
                    coreReference.get().close();
                } catch (Throwable error) {
                    failure.set(error);
                } finally {
                    callbackCompleted.countDown();
                }
            },
            Executors.newSingleThreadExecutor());
        coreReference.set(core);
        core.start(session("close-drain"));
        bindings.callbacks.onFrame(directBuffer(1), 1, 1, 1, false, false);
        callbackCompleted.await();

        assertEquals(
            "BeaconStreamCore cannot close from its frame sink callback.",
            failure.get().getMessage());
        assertEquals(0, bindings.releaseCount);
        core.close();
        assertEquals(1, bindings.releaseCount);
    }

    @Test
    public void frameDeliveryDoesNotFabricateQueueDepthFeedback() throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        AtomicInteger order = new AtomicInteger();
        AtomicReference<Integer> frameOrder = new AtomicReference<>();
        CountDownLatch frameDelivered = new CountDownLatch(1);
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> {
                frameOrder.set(order.incrementAndGet());
                frameDelivered.countDown();
            },
            Executors.newSingleThreadExecutor());
        core.start(session("feedback"));
        bindings.feedbackOrder = order;
        bindings.callbacks.onFrame(directBuffer(1), 1, 1, 1, false, false);
        frameDelivered.await();
        assertEquals(-1, bindings.lastQueuedAccessUnits);
        assertEquals(-1, bindings.lastDroppedAccessUnits);
        assertEquals(Integer.valueOf(1), frameOrder.get());
        assertEquals(1, order.get());
        core.close();
    }

    @Test
    public void feedbackObserverRunsAfterSuccessfulNativeFeedbackHandoff() throws Exception {
        RecordingBindings bindings = new RecordingBindings();
        CountDownLatch frameDelivered = new CountDownLatch(1);
        CountDownLatch observerCalled = new CountDownLatch(1);
        AtomicInteger order = new AtomicInteger();
        AtomicReference<Integer> frameOrder = new AtomicReference<>();
        AtomicReference<Integer> observerOrder = new AtomicReference<>();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> {
                frameOrder.set(order.incrementAndGet());
                frameDelivered.countDown();
            },
            Executors.newSingleThreadExecutor(),
            () -> {
                observerOrder.set(order.incrementAndGet());
                observerCalled.countDown();
            });
        bindings.feedbackOrder = order;
        core.start(session("feedback-observer"));

        bindings.callbacks.onFrame(directBuffer(1), 1, 1, 1, false, false);
        frameDelivered.await();
        core.sendQueueDepthFeedback(1, 0, 0);
        observerCalled.await();

        assertEquals(Integer.valueOf(1), frameOrder.get());
        assertEquals(Integer.valueOf(3), observerOrder.get());
        assertEquals(3, order.get());
        core.close();
    }

    @Test
    public void typedVideoFeedbackIsGenerationScopedAcrossReconnect() {
        RecordingBindings bindings = new RecordingBindings();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> { },
            Executors.newSingleThreadExecutor());
        core.start(session("typed-feedback", "AQID"));

        core.sendQueueDepthFeedback(1, 2, 3);
        core.sendDecoderFeedback(
            1, BeaconStreamCore.DecoderState.FAILED, 321);
        core.sendRenderedFrameFeedback(1, 44, 55, 66);
        core.requestDecoderIdr(1, 44);

        core.start(session("typed-feedback", "BAUG"));
        core.sendQueueDepthFeedback(1, 9, 9);
        core.sendDecoderFeedback(
            1, BeaconStreamCore.DecoderState.READY, 0);
        core.sendRenderedFrameFeedback(1, 99, 99, 99);
        core.requestDecoderIdr(1, 99);

        assertEquals(
            Arrays.asList("queue:1:2:3", "decoder:1:FAILED:321",
                "rendered:1:44:55:66", "idr:1:44"),
            bindings.typedFeedback);
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

        core.sendQueueDepthFeedback(1, 0, 0);
        failureObserved.await();

        assertEquals("feedback", failureStage.get());
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
        core.sendQueueDepthFeedback(1, 0, 0);
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

    private static ByteBuffer directBuffer(int... values) {
        ByteBuffer buffer = ByteBuffer.allocateDirect(values.length);
        for (int value : values) buffer.put((byte) value);
        buffer.flip();
        return buffer;
    }

    private static byte[] bytes(ByteBuffer source) {
        ByteBuffer copy = source.asReadOnlyBuffer();
        byte[] values = new byte[copy.remaining()];
        copy.get(values);
        return values;
    }

    private static BeaconStreamSession session(String sessionId, String ticket) {
        return BeaconStreamSession.parse(
            "https://beacon.example",
            "client",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"" + ticket + "\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":1,\"planExplanation\":\"selected\",\"sessionId\":\"" + sessionId + "\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"h264\",\"width\":1280,\"height\":720,\"framesPerSecondNumerator\":60,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"sdr\"},\"selectedAudio\":{\"codec\":\"opus\",\"sampleRateHz\":48000,\"channelCount\":2,\"frameDurationUs\":20000,\"bitrateBps\":96000}}}");
    }

    private static BeaconStreamSession benchmarkSession() {
        return BeaconStreamSession.parse(
            "https://beacon.example",
            "client",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":9,\"planExplanation\":\"preflight\",\"sessionId\":\"benchmark:3c13df40-26c4-40c6-8414-268734f1024d\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"benchmark\":{\"runId\":\"3c13df40-26c4-40c6-8414-268734f1024d\",\"schemaVersion\":1,\"reliableRound\":{\"packetCount\":16,\"payloadBytes\":32768,\"measurementIntervalUs\":250000},\"datagramRound\":{\"packetCount\":64,\"payloadBytes\":1000,\"measurementIntervalUs\":250000},\"runToken\":\"AAECAwQFBgcICQoLDA0ODw==\"}}}");
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
        AtomicInteger feedbackOrder;
        final List<String> typedFeedback = new ArrayList<>();
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
            lastQueuedAccessUnits = queuedAccessUnits;
            lastDroppedAccessUnits = droppedAccessUnits;
            if (feedbackOrder != null) feedbackOrder.incrementAndGet();
            typedFeedback.add(
                "queue:" + generation + ":" + queuedAccessUnits + ":" +
                    droppedAccessUnits);
        }
        @Override public void sendDecoderFeedback(
            long handle,
            long generation,
            BeaconStreamCore.DecoderState state,
            int platformErrorCode) {
            typedFeedback.add(
                "decoder:" + generation + ":" + state + ":" + platformErrorCode);
        }
        @Override public void sendRenderedFrameFeedback(
            long handle,
            long generation,
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) {
            typedFeedback.add(
                "rendered:" + generation + ":" + frameSequence + ":" +
                    presentationTimeUs + ":" + renderedAtUs);
        }
        @Override public void requestDecoderIdr(
            long handle,
            long generation,
            long lastCompleteSequence) {
            typedFeedback.add("idr:" + generation + ":" + lastCompleteSequence);
        }
        @Override public void replaceSurface(long handle, Object surface) { }
        @Override public void stop(long handle) {
            stopCount++;
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
