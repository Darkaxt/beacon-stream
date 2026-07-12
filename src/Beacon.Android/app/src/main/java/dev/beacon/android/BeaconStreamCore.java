package dev.beacon.android;

import java.util.Arrays;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.CountDownLatch;

public final class BeaconStreamCore implements AutoCloseable {
    private final Bindings bindings;
    private final EncodedFrameSink sink;
    private final ExecutorService callbackExecutor;
    private final FeedbackObserver feedbackObserver;
    private final FailureObserver failureObserver;
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
        this(
            new JniBindings(),
            sink,
            Executors.newSingleThreadExecutor(r -> new Thread(r, "beacon-frame-callback")),
            feedbackObserver,
            failureObserver);
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
        if (bindings == null || sink == null || callbackExecutor == null ||
            feedbackObserver == null || failureObserver == null) {
            throw new IllegalArgumentException("BeaconStreamCore dependencies are required.");
        }
        this.bindings = bindings;
        this.sink = sink;
        this.callbackExecutor = callbackExecutor;
        this.feedbackObserver = feedbackObserver;
        this.failureObserver = failureObserver;
        this.handle = bindings.create(new NativeCallbacks() {
            @Override public void onFrame(
                byte[] bytes, long presentationTimeUs, long sequence, long generation) {
                dispatchFrame(bytes, presentationTimeUs, sequence, generation);
            }

            @Override public void onConnectionLost(long generation) {
                boolean accepted = false;
                synchronized (BeaconStreamCore.this) {
                    long expectedGeneration = startingGeneration != 0
                        ? startingGeneration : activeGeneration;
                    if (!open || generation == 0 || generation != expectedGeneration ||
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
        });
        if (handle == 0) {
            callbackExecutor.shutdown();
            throw new IllegalStateException("Could not create Beacon native StreamCore.");
        }
    }

    public void start(BeaconStreamSession session) {
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
                return;
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
            grant.clearTicket();
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
        return nativeCreate(callbacks);
    }

    static void emitNativeFrameForTest(long handle, byte[] bytes, long presentationTimeUs) {
        nativeTestEmitFrame(handle, bytes, presentationTimeUs);
    }

    static void releaseNativeHandleForTest(long handle) {
        nativeRelease(handle);
    }

    static void awaitNativeRegistryIdleForTest() {
        nativeTestAwaitRegistryIdle();
    }

    static int nativeRegistrySizeForTest() {
        return nativeTestRegistrySize();
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
        byte[] bytes, long presentationTimeUs, long sequence, long generation) {
        final byte[] copy = Arrays.copyOf(bytes, bytes.length);
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
                    sink.onFrame(new EncodedFrame(copy, presentationTimeUs, sequence));
                } finally {
                    IN_SINK_CALLBACK.remove();
                }
                if (beginNativeCallIfOpen(generation)) {
                    try {
                        bindings.sendQueueDepthFeedback(handle, generation, 0, 0);
                        feedbackObserver.onQueueDepthFeedbackSent();
                    } catch (RuntimeException | Error error) {
                        reportFailure("feedback");
                        throw error;
                    } finally {
                        endNativeCall();
                    }
                }
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

    public static final class EncodedFrame {
        public final byte[] bytes;
        public final long presentationTimeUs;
        public final long sequence;

        EncodedFrame(byte[] bytes, long presentationTimeUs, long sequence) {
            this.bytes = bytes;
            this.presentationTimeUs = presentationTimeUs;
            this.sequence = sequence;
        }
    }

    interface NativeCallbacks {
        void onFrame(byte[] bytes, long presentationTimeUs, long sequence, long generation);
        void onConnectionLost(long generation);
    }

    interface FeedbackObserver {
        void onQueueDepthFeedbackSent();
    }

    interface FailureObserver {
        void onFailure(String stage);
    }

    interface Bindings {
        long create(NativeCallbacks callbacks);
        boolean start(long handle, BeaconStreamSession.NativeGrant grant);
        void sendInput(long handle, BeaconApiClient.InputBatch input);
        default void sendQueueDepthFeedback(
            long handle, long generation, int queuedAccessUnits,
            long droppedAccessUnits) { }
        void replaceSurface(long handle, Object surface);
        void stop(long handle);
        void release(long handle);
    }

    private static final class JniBindings implements Bindings {
        static { System.loadLibrary("beacon_streamcore"); }

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
        @Override public void replaceSurface(long handle, Object surface) { nativeReplaceSurface(handle, surface); }
        @Override public void stop(long handle) { nativeStop(handle); }
        @Override public void release(long handle) { nativeRelease(handle); }
    }

    private static native long nativeCreate(NativeCallbacks callbacks);
    private static native boolean nativeStart(long handle, BeaconStreamSession.NativeGrant grant);
    private static native void nativeSendInput(long handle, BeaconApiClient.InputBatch input);
    private static native void nativeSendQueueDepthFeedback(
        long handle, long generation, int queuedAccessUnits,
        long droppedAccessUnits);
    private static native void nativeReplaceSurface(long handle, Object surface);
    private static native void nativeStop(long handle);
    private static native void nativeRelease(long handle);
    private static native void nativeTestEmitFrame(
        long handle, byte[] bytes, long presentationTimeUs);
    private static native void nativeTestAwaitRegistryIdle();
    private static native int nativeTestRegistrySize();
}
