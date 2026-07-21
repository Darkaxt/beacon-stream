package dev.beacon.android;

import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.CountDownLatch;

public final class BeaconStreamCore implements AutoCloseable {
    private final Bindings bindings;
    private final EncodedFrameSink sink;
    private final ExecutorService callbackExecutor;
    private final FeedbackObserver feedbackObserver;
    private final FailureObserver failureObserver;
    private final BenchmarkObserver benchmarkObserver;
    private final long handle;
    private boolean open = true;
    private boolean stopped;
    private boolean released;
    private boolean closing;
    private boolean lifecycleBusy;
    private int nativeCalls;
    private BeaconStreamSession activeGrant;
    private long nextGeneration = 1;
    private long activeGeneration;
    private long startingGeneration;
    private long lossDuringStartGeneration;
    private long lastLossGeneration;
    private int connectionLossCount;
    private final CountDownLatch closeComplete = new CountDownLatch(1);
    private static final ThreadLocal<Boolean> IN_SINK_CALLBACK = new ThreadLocal<>();

    public BeaconStreamCore(EncodedFrameSink sink) {
        this(sink, () -> { });
    }

    BeaconStreamCore(EncodedFrameSink sink, FeedbackObserver feedbackObserver) {
        this(sink, feedbackObserver, stage -> { });
    }

    BeaconStreamCore(
        EncodedFrameSink sink,
        FeedbackObserver feedbackObserver,
        FailureObserver failureObserver) {
        this(sink, feedbackObserver, failureObserver, result -> { });
    }

    BeaconStreamCore(
        EncodedFrameSink sink,
        FeedbackObserver feedbackObserver,
        FailureObserver failureObserver,
        BenchmarkObserver benchmarkObserver) {
        this(
            new JniBindings(),
            sink,
            Executors.newSingleThreadExecutor(r -> new Thread(r, "beacon-frame-callback")),
            feedbackObserver,
            failureObserver,
            benchmarkObserver);
    }

    BeaconStreamCore(Bindings bindings, EncodedFrameSink sink, ExecutorService callbackExecutor) {
        this(bindings, sink, callbackExecutor, () -> { }, stage -> { });
    }

    BeaconStreamCore(
        Bindings bindings,
        EncodedFrameSink sink,
        ExecutorService callbackExecutor,
        FeedbackObserver feedbackObserver) {
        this(bindings, sink, callbackExecutor, feedbackObserver, stage -> { });
    }

    BeaconStreamCore(
        Bindings bindings,
        EncodedFrameSink sink,
        ExecutorService callbackExecutor,
        FeedbackObserver feedbackObserver,
        FailureObserver failureObserver) {
        this(bindings, sink, callbackExecutor, feedbackObserver, failureObserver, result -> { });
    }

    BeaconStreamCore(
        Bindings bindings,
        EncodedFrameSink sink,
        ExecutorService callbackExecutor,
        FeedbackObserver feedbackObserver,
        FailureObserver failureObserver,
        BenchmarkObserver benchmarkObserver) {
        if (bindings == null || sink == null || callbackExecutor == null ||
            feedbackObserver == null || failureObserver == null || benchmarkObserver == null) {
            throw new IllegalArgumentException("BeaconStreamCore dependencies are required.");
        }
        this.bindings = bindings;
        this.sink = sink;
        this.callbackExecutor = callbackExecutor;
        this.feedbackObserver = feedbackObserver;
        this.failureObserver = failureObserver;
        this.benchmarkObserver = benchmarkObserver;
        this.handle = bindings.create(new NativeCallbacks() {
            @Override public void onFrame(
                ByteBuffer bytes,
                long presentationTimeUs,
                long sequence,
                long generation,
                boolean idr,
                boolean codecConfiguration) {
                dispatchFrame(
                    bytes,
                    presentationTimeUs,
                    sequence,
                    generation,
                    idr,
                    codecConfiguration);
            }

            @Override public void onConnectionLost(long generation) {
                boolean accepted = false;
                synchronized (BeaconStreamCore.this) {
                    long expectedGeneration = startingGeneration != 0
                        ? startingGeneration : activeGeneration;
                    if (!open || stopped || generation == 0 || generation != expectedGeneration ||
                        generation == lastLossGeneration) {
                        return;
                    }
                    lastLossGeneration = generation;
                    connectionLossCount++;
                    stopped = true;
                    if (generation == startingGeneration) {
                        lossDuringStartGeneration = generation;
                    }
                    BeaconStreamCore.this.notifyAll();
                    accepted = true;
                }
                if (accepted) reportFailure("transport");
            }

            @Override public void onBenchmarkCompleted(
                double sustainableThroughputMbps,
                long[] sequences,
                int[] payloadBytes,
                long[] rttUs,
                long[] jitterUs,
                int[] reorderDistances,
                boolean[] received,
                long generation) {
                dispatchBenchmarkResult(
                    sustainableThroughputMbps,
                    sequences,
                    payloadBytes,
                    rttUs,
                    jitterUs,
                    reorderDistances,
                    received,
                    generation);
            }
        });
        if (handle == 0) {
            callbackExecutor.shutdown();
            throw new IllegalStateException("Could not create Beacon native StreamCore.");
        }
    }

    public long start(BeaconStreamSession session) {
        if (session == null) {
            throw new IllegalArgumentException("Beacon connection grant is required.");
        }
        BeaconStreamSession.NativeGrant grant;
        boolean stopBeforeStart;
        long generation;
        synchronized (this) {
            awaitQuiescentLocked();
            requireOpen();
            if (session == activeGrant && session.ticketConsumed()) {
                return activeGeneration;
            }
            generation = nextGeneration++;
            if (generation <= 0) {
                throw new IllegalStateException("Beacon connection generation exhausted.");
            }
            grant = session.consumeNativeGrant(generation);
            lifecycleBusy = true;
            startingGeneration = generation;
            stopBeforeStart = !stopped && activeGrant != null;
            if (stopBeforeStart) {
                stopped = true;
                activeGrant = null;
            }
        }
        boolean started = false;
        try {
            if (stopBeforeStart) bindings.stop(handle);
            if (!bindings.start(handle, grant)) {
                throw new IllegalStateException(
                    "Native StreamCore rejected generation " + generation + ".");
            }
            started = true;
        } finally {
            grant.clearSecrets();
            synchronized (this) {
                stopped = !started || lossDuringStartGeneration == generation;
                activeGrant = started ? session : null;
                activeGeneration = started ? generation : 0;
                startingGeneration = 0;
                lossDuringStartGeneration = 0;
                lifecycleBusy = false;
                notifyAll();
            }
        }
        return generation;
    }

    public void sendInput(BeaconApiClient.InputBatch input) {
        if (input == null) {
            throw new IllegalArgumentException("Beacon input batch is required.");
        }
        beginNativeCall();
        try {
            bindings.sendInput(handle, input);
        } finally {
            endNativeCall();
        }
    }

    public void sendQueueDepthFeedback(
        long generation,
        int queuedAccessUnits,
        long droppedAccessUnits) {
        if (generation <= 0 || queuedAccessUnits < 0 || droppedAccessUnits < 0) {
            throw new IllegalArgumentException("Queue-depth feedback values are invalid.");
        }
        sendFeedback(
            generation,
            () -> bindings.sendQueueDepthFeedback(
                handle, generation, queuedAccessUnits, droppedAccessUnits),
            true);
    }

    public void sendDecoderFeedback(
        long generation,
        DecoderState state,
        int platformErrorCode) {
        if (generation <= 0 || state == null || platformErrorCode < 0) {
            throw new IllegalArgumentException("Decoder feedback values are invalid.");
        }
        sendFeedback(
            generation,
            () -> bindings.sendDecoderFeedback(
                handle, generation, state, platformErrorCode),
            false);
    }

    public void sendRenderedFrameFeedback(
        long generation,
        long frameSequence,
        long presentationTimeUs,
        long renderedAtUs) {
        if (generation <= 0 || frameSequence <= 0 ||
            presentationTimeUs < 0 || renderedAtUs < 0) {
            throw new IllegalArgumentException("Rendered-frame feedback values are invalid.");
        }
        sendFeedback(
            generation,
            () -> bindings.sendRenderedFrameFeedback(
                handle,
                generation,
                frameSequence,
                presentationTimeUs,
                renderedAtUs),
            false);
    }

    public void requestDecoderIdr(
        long generation,
        long lastCompleteSequence) {
        if (generation <= 0 || lastCompleteSequence < 0) {
            throw new IllegalArgumentException("Decoder IDR request values are invalid.");
        }
        sendFeedback(
            generation,
            () -> bindings.requestDecoderIdr(
                handle, generation, lastCompleteSequence),
            false);
    }

    public void replaceSurface(Object surface) {
        beginNativeCall();
        try {
            bindings.replaceSurface(handle, surface);
        } finally {
            endNativeCall();
        }
    }

    public void stop() {
        synchronized (this) {
            awaitQuiescentLocked();
            if (!open || stopped) return;
            lifecycleBusy = true;
            stopped = true;
        }
        try {
            bindings.stop(handle);
        } finally {
            synchronized (this) {
                lifecycleBusy = false;
                notifyAll();
            }
        }
    }

    public synchronized boolean isOpen() { return open; }

    boolean callbackExecutorShutdown() { return callbackExecutor.isShutdown(); }

    synchronized int connectionLossCountForTest() { return connectionLossCount; }

    synchronized void awaitConnectionLossForTest(int expectedCount) throws InterruptedException {
        if (expectedCount <= 0) {
            throw new IllegalArgumentException("Expected connection-loss count must be positive.");
        }
        while (connectionLossCount < expectedCount) {
            wait();
        }
    }

    synchronized boolean stoppedForTest() { return stopped; }

    synchronized long activeGenerationForTest() { return activeGeneration; }

    static long createNativeHandleForTest(NativeCallbacks callbacks) {
        NativeLibrary.ensureLoaded();
        return nativeCreate(callbacks);
    }

    static void emitNativeFrameForTest(long handle, byte[] bytes, long presentationTimeUs) {
        NativeLibrary.ensureLoaded();
        nativeTestEmitFrame(handle, bytes, presentationTimeUs);
    }

    static void releaseNativeHandleForTest(long handle) {
        NativeLibrary.ensureLoaded();
        nativeRelease(handle);
    }

    static void awaitNativeRegistryIdleForTest() {
        NativeLibrary.ensureLoaded();
        nativeTestAwaitRegistryIdle();
    }

    static int nativeRegistrySizeForTest() {
        NativeLibrary.ensureLoaded();
        return nativeTestRegistrySize();
    }

    static boolean parseNativeGrantForTest(BeaconStreamSession.NativeGrant grant) {
        NativeLibrary.ensureLoaded();
        return nativeTestParseGrant(grant);
    }

    static void emitNativeBenchmarkResultForTest(long handle, long generation) {
        NativeLibrary.ensureLoaded();
        nativeTestEmitBenchmarkResult(handle, generation);
    }

    @Override
    public void close() {
        if (Boolean.TRUE.equals(IN_SINK_CALLBACK.get())) {
            throw new IllegalStateException("BeaconStreamCore cannot close from its frame sink callback.");
        }
        CountDownLatch drained = new CountDownLatch(1);
        boolean ownsClose = false;
        boolean stopBeforeRelease = false;
        synchronized (this) {
            if (released) return;
            if (!closing) {
                closing = true;
                open = false;
                ownsClose = true;
            }
        }
        if (!ownsClose) {
            await(closeComplete);
            return;
        }
        synchronized (this) {
            awaitQuiescentLocked();
            stopBeforeRelease = !stopped;
            stopped = true;
            lifecycleBusy = true;
        }
        Throwable failure = null;
        try {
            if (stopBeforeRelease) bindings.stop(handle);
        } catch (RuntimeException | Error error) {
            failure = error;
        }
        try {
            callbackExecutor.execute(drained::countDown);
            callbackExecutor.shutdown();
            await(drained);
        } catch (RuntimeException | Error error) {
            if (failure == null) failure = error;
            else failure.addSuppressed(error);
        }
        try {
            bindings.release(handle);
        } catch (RuntimeException | Error error) {
            if (failure == null) failure = error;
            else failure.addSuppressed(error);
        } finally {
            synchronized (this) {
                released = true;
                lifecycleBusy = false;
                notifyAll();
                closeComplete.countDown();
            }
        }
        if (failure instanceof RuntimeException) throw (RuntimeException) failure;
        if (failure instanceof Error) throw (Error) failure;
    }

    private void dispatchFrame(
        ByteBuffer bytes,
        long presentationTimeUs,
        long sequence,
        long generation,
        boolean idr,
        boolean codecConfiguration) {
        EncodedFrame frame = new EncodedFrame(
            bytes,
            presentationTimeUs,
            sequence,
            idr,
            codecConfiguration);
        synchronized (this) {
            long expectedGeneration = startingGeneration != 0
                ? startingGeneration : activeGeneration;
            if (!open || stopped || generation == 0 ||
                generation != expectedGeneration) {
                return;
            }
            callbackExecutor.execute(() -> {
                synchronized (BeaconStreamCore.this) {
                    if (!open || stopped || generation != activeGeneration) {
                        return;
                    }
                }
                IN_SINK_CALLBACK.set(true);
                try {
                    sink.onFrame(frame);
                } finally {
                    IN_SINK_CALLBACK.remove();
                }
            });
        }
    }

    private void dispatchBenchmarkResult(
        double sustainableThroughputMbps,
        long[] sequences,
        int[] payloadBytes,
        long[] rttUs,
        long[] jitterUs,
        int[] reorderDistances,
        boolean[] received,
        long generation) {
        if (sequences == null || payloadBytes == null || rttUs == null ||
            jitterUs == null || reorderDistances == null || received == null ||
            payloadBytes.length != sequences.length || rttUs.length != sequences.length ||
            jitterUs.length != sequences.length || reorderDistances.length != sequences.length ||
            received.length != sequences.length || !Double.isFinite(sustainableThroughputMbps) ||
            sustainableThroughputMbps <= 0) {
            reportFailure("benchmark");
            return;
        }
        List<BenchmarkNetworkSample> samples = new ArrayList<>(sequences.length);
        for (int index = 0; index < sequences.length; index++) {
            samples.add(new BenchmarkNetworkSample(
                sequences[index],
                payloadBytes[index],
                rttUs[index],
                jitterUs[index],
                reorderDistances[index],
                received[index]));
        }
        BenchmarkNetworkResult result = new BenchmarkNetworkResult(
            sustainableThroughputMbps,
            samples);
        synchronized (this) {
            long expectedGeneration = startingGeneration != 0
                ? startingGeneration : activeGeneration;
            if (!open || stopped || generation == 0 || generation != expectedGeneration) {
                return;
            }
            callbackExecutor.execute(() -> {
                synchronized (BeaconStreamCore.this) {
                    if (!open || stopped || generation != activeGeneration) {
                        return;
                    }
                }
                benchmarkObserver.onCompleted(result);
            });
        }
    }

    private void beginNativeCall() {
        synchronized (this) {
            awaitLifecycleIdleLocked();
            requireOpen();
            nativeCalls++;
        }
    }

    private boolean beginNativeCallIfOpen(long expectedGeneration) {
        synchronized (this) {
            if (!open || stopped || expectedGeneration != activeGeneration) return false;
            awaitLifecycleIdleLocked();
            if (!open || stopped || expectedGeneration != activeGeneration) return false;
            nativeCalls++;
            return true;
        }
    }

    private void endNativeCall() {
        synchronized (this) {
            nativeCalls--;
            notifyAll();
        }
    }

    private void sendFeedback(
        long generation,
        Runnable sender,
        boolean notifyQueueObserver) {
        if (!beginNativeCallIfOpen(generation)) return;
        try {
            sender.run();
            if (notifyQueueObserver) feedbackObserver.onQueueDepthFeedbackSent();
        } catch (RuntimeException | Error failure) {
            reportFailure("feedback");
        } finally {
            endNativeCall();
        }
    }

    private void awaitLifecycleIdleLocked() {
        boolean interrupted = false;
        while (lifecycleBusy) {
            try {
                wait();
            } catch (InterruptedException error) {
                interrupted = true;
            }
        }
        if (interrupted) Thread.currentThread().interrupt();
    }

    private void awaitQuiescentLocked() {
        boolean interrupted = false;
        while (lifecycleBusy || nativeCalls != 0) {
            try {
                wait();
            } catch (InterruptedException error) {
                interrupted = true;
            }
        }
        if (interrupted) Thread.currentThread().interrupt();
    }

    private static void await(CountDownLatch latch) {
        boolean interrupted = false;
        while (true) {
            try {
                latch.await();
                break;
            } catch (InterruptedException error) {
                interrupted = true;
            }
        }
        if (interrupted) Thread.currentThread().interrupt();
    }

    private void requireOpen() {
        if (!open) {
            throw new IllegalStateException("BeaconStreamCore is closed.");
        }
    }

    private void reportFailure(String stage) {
        try {
            failureObserver.onFailure(stage);
        } catch (RuntimeException | Error ignored) {
        }
    }

    public interface EncodedFrameSink {
        void onFrame(EncodedFrame frame);
    }

    public enum DecoderState {
        READY(1),
        AWAITING_IDR(2),
        FAILED(3);

        final int nativeValue;

        DecoderState(int nativeValue) {
            this.nativeValue = nativeValue;
        }
    }

    public static final class EncodedFrame {
        public final ByteBuffer bytes;
        public final long presentationTimeUs;
        public final long sequence;
        public final boolean idr;
        public final boolean codecConfiguration;

        EncodedFrame(
            ByteBuffer bytes,
            long presentationTimeUs,
            long sequence,
            boolean idr,
            boolean codecConfiguration) {
            if (bytes == null || !bytes.isDirect()) {
                throw new IllegalArgumentException("Encoded frame requires a direct buffer.");
            }
            this.bytes = readOnlySlice(bytes);
            this.presentationTimeUs = presentationTimeUs;
            this.sequence = sequence;
            this.idr = idr;
            this.codecConfiguration = codecConfiguration;
        }

        private static ByteBuffer readOnlySlice(ByteBuffer source) {
            return source.asReadOnlyBuffer().slice().asReadOnlyBuffer();
        }
    }

    public static final class BenchmarkNetworkResult {
        public final double sustainableThroughputMbps;
        public final List<BenchmarkNetworkSample> samples;

        BenchmarkNetworkResult(
            double sustainableThroughputMbps,
            List<BenchmarkNetworkSample> samples) {
            this.sustainableThroughputMbps = sustainableThroughputMbps;
            this.samples = Collections.unmodifiableList(new ArrayList<>(samples));
        }
    }

    public static final class BenchmarkNetworkSample {
        public final long sequence;
        public final int payloadBytes;
        public final long rttUs;
        public final long jitterUs;
        public final int reorderDistance;
        public final boolean received;

        BenchmarkNetworkSample(
            long sequence,
            int payloadBytes,
            long rttUs,
            long jitterUs,
            int reorderDistance,
            boolean received) {
            this.sequence = sequence;
            this.payloadBytes = payloadBytes;
            this.rttUs = rttUs;
            this.jitterUs = jitterUs;
            this.reorderDistance = reorderDistance;
            this.received = received;
        }
    }

    interface NativeCallbacks {
        void onFrame(
            ByteBuffer bytes,
            long presentationTimeUs,
            long sequence,
            long generation,
            boolean idr,
            boolean codecConfiguration);
        void onConnectionLost(long generation);
        default void onBenchmarkCompleted(
            double sustainableThroughputMbps,
            long[] sequences,
            int[] payloadBytes,
            long[] rttUs,
            long[] jitterUs,
            int[] reorderDistances,
            boolean[] received,
            long generation) { }
    }

    interface FeedbackObserver {
        void onQueueDepthFeedbackSent();
    }

    interface FailureObserver {
        void onFailure(String stage);
    }

    interface BenchmarkObserver {
        void onCompleted(BenchmarkNetworkResult result);
    }

    interface Bindings {
        long create(NativeCallbacks callbacks);
        boolean start(long handle, BeaconStreamSession.NativeGrant grant);
        void sendInput(long handle, BeaconApiClient.InputBatch input);
        default void sendQueueDepthFeedback(
            long handle, long generation, int queuedAccessUnits,
            long droppedAccessUnits) { }
        default void sendDecoderFeedback(
            long handle,
            long generation,
            DecoderState state,
            int platformErrorCode) { }
        default void sendRenderedFrameFeedback(
            long handle,
            long generation,
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) { }
        default void requestDecoderIdr(
            long handle,
            long generation,
            long lastCompleteSequence) { }
        void replaceSurface(long handle, Object surface);
        void stop(long handle);
        void release(long handle);
    }

    private static final class JniBindings implements Bindings {
        static { NativeLibrary.ensureLoaded(); }

        @Override public long create(NativeCallbacks callbacks) { return nativeCreate(callbacks); }
        @Override public boolean start(long handle, BeaconStreamSession.NativeGrant grant) {
            return nativeStart(handle, grant);
        }
        @Override public void sendInput(long handle, BeaconApiClient.InputBatch input) { nativeSendInput(handle, input); }
        @Override public void sendQueueDepthFeedback(
            long handle, long generation, int queuedAccessUnits,
            long droppedAccessUnits) {
            nativeSendQueueDepthFeedback(
                handle, generation, queuedAccessUnits, droppedAccessUnits);
        }
        @Override public void sendDecoderFeedback(
            long handle,
            long generation,
            DecoderState state,
            int platformErrorCode) {
            nativeSendDecoderFeedback(
                handle, generation, state.nativeValue, platformErrorCode);
        }
        @Override public void sendRenderedFrameFeedback(
            long handle,
            long generation,
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) {
            nativeSendRenderedFrameFeedback(
                handle,
                generation,
                frameSequence,
                presentationTimeUs,
                renderedAtUs);
        }
        @Override public void requestDecoderIdr(
            long handle,
            long generation,
            long lastCompleteSequence) {
            nativeRequestDecoderIdr(handle, generation, lastCompleteSequence);
        }
        @Override public void replaceSurface(long handle, Object surface) { nativeReplaceSurface(handle, surface); }
        @Override public void stop(long handle) { nativeStop(handle); }
        @Override public void release(long handle) { nativeRelease(handle); }
    }

    private static final class NativeLibrary {
        static { System.loadLibrary("beacon_streamcore"); }

        static void ensureLoaded() { }
    }

    private static native long nativeCreate(NativeCallbacks callbacks);
    private static native boolean nativeStart(long handle, BeaconStreamSession.NativeGrant grant);
    private static native void nativeSendInput(long handle, BeaconApiClient.InputBatch input);
    private static native void nativeSendQueueDepthFeedback(
        long handle, long generation, int queuedAccessUnits,
        long droppedAccessUnits);
    private static native void nativeSendDecoderFeedback(
        long handle, long generation, int state, int platformErrorCode);
    private static native void nativeSendRenderedFrameFeedback(
        long handle,
        long generation,
        long frameSequence,
        long presentationTimeUs,
        long renderedAtUs);
    private static native void nativeRequestDecoderIdr(
        long handle, long generation, long lastCompleteSequence);
    private static native void nativeReplaceSurface(long handle, Object surface);
    private static native void nativeStop(long handle);
    private static native void nativeRelease(long handle);
    private static native void nativeTestEmitFrame(
        long handle, byte[] bytes, long presentationTimeUs);
    private static native void nativeTestAwaitRegistryIdle();
    private static native int nativeTestRegistrySize();
    private static native boolean nativeTestParseGrant(BeaconStreamSession.NativeGrant grant);
    private static native void nativeTestEmitBenchmarkResult(long handle, long generation);
}
