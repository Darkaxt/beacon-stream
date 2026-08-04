package dev.beacon.android;

import android.graphics.Bitmap;
import android.os.Handler;
import android.os.Looper;
import android.view.PixelCopy;
import android.view.SurfaceView;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;

final class Gate5SurfaceEvidence implements BeaconVideoFeedbackBridge.Observer, AutoCloseable {
    private static final int EvidenceWidth = 64;
    private static final int EvidenceHeight = 40;
    private static final int ExpectedFrames = 12;

    private final Object gate = new Object();
    private final SurfaceView surfaceView;
    private final Gate3SessionEvidence sessionEvidence;
    private final Handler pixelCopyHandler = new Handler(Looper.getMainLooper());
    private final CountDownLatch completed = new CountDownLatch(ExpectedFrames);
    private final List<Long> checksums = new ArrayList<>();
    private boolean pixelCopyPending;
    private boolean closed;
    private Throwable failure;

    Gate5SurfaceEvidence(SurfaceView surfaceView, Gate3SessionEvidence sessionEvidence) {
        if (surfaceView == null || sessionEvidence == null) {
            throw new IllegalArgumentException("Gate 5 Surface evidence dependencies are required.");
        }
        this.surfaceView = surfaceView;
        this.sessionEvidence = sessionEvidence;
    }

    @Override
    public void onQueueDepthSent(
        long generation,
        int queuedAccessUnits,
        long droppedAccessUnits) {
        sessionEvidence.onQueueDepthSent(generation, queuedAccessUnits, droppedAccessUnits);
    }

    @Override
    public void onDecoderStateSent(
        long generation,
        BeaconStreamCore.DecoderState state,
        int platformErrorCode) {
        sessionEvidence.onDecoderStateSent(generation, state, platformErrorCode);
    }

    @Override
    public void onRenderedFrameSent(
        long generation,
        long frameSequence,
        long presentationTimeUs,
        long renderedAtUs) {
        sessionEvidence.onRenderedFrameSent(
            generation, frameSequence, presentationTimeUs, renderedAtUs);
        synchronized (gate) {
            if (closed || failure != null || checksums.size() >= ExpectedFrames || pixelCopyPending) {
                return;
            }
            pixelCopyPending = true;
        }

        Bitmap bitmap = Bitmap.createBitmap(
            EvidenceWidth, EvidenceHeight, Bitmap.Config.ARGB_8888);
        try {
            PixelCopy.request(
                surfaceView,
                bitmap,
                result -> finishPixelCopy(result, bitmap, presentationTimeUs),
                pixelCopyHandler);
        } catch (RuntimeException copyFailure) {
            bitmap.recycle();
            synchronized (gate) {
                pixelCopyPending = false;
            }
            recordFailure(copyFailure);
        }
    }

    @Override
    public void onIdrRequested(long generation, long lastCompleteSequence) {
        sessionEvidence.onIdrRequested(generation, lastCompleteSequence);
    }

    void awaitChangingFrames() throws InterruptedException {
        sessionEvidence.awaitRenderedFrameFeedback();
        completed.await();
        Throwable observedFailure;
        List<Long> observedChecksums;
        synchronized (gate) {
            observedFailure = failure;
            observedChecksums = List.copyOf(checksums);
        }
        if (observedFailure != null) {
            throw new AssertionError("Gate 5 Surface evidence failed.", observedFailure);
        }
        if (observedChecksums.size() != ExpectedFrames) {
            throw new AssertionError(
                "Expected " + ExpectedFrames + " Gate 5 frames but observed " +
                    observedChecksums.size() + ".");
        }
        if (observedChecksums.stream().anyMatch(value -> value == 0)) {
            throw new AssertionError("Gate 5 rendered a blank Surface frame.");
        }
        if (observedChecksums.stream().distinct().count() < 2) {
            throw new AssertionError("Gate 5 Surface pixels did not change.");
        }
    }

    int frameCount() {
        synchronized (gate) {
            return checksums.size();
        }
    }

    long pixelVariantCount() {
        synchronized (gate) {
            return checksums.stream().distinct().count();
        }
    }

    void recordFailure(Throwable observedFailure) {
        synchronized (gate) {
            if (failure != null || closed) return;
            failure = observedFailure == null
                ? new IllegalStateException("Gate 5 Surface evidence failed without a cause.")
                : observedFailure;
            pixelCopyPending = false;
        }
        while (completed.getCount() > 0) completed.countDown();
    }

    @Override
    public void close() {
        synchronized (gate) {
            closed = true;
        }
    }

    private void finishPixelCopy(int result, Bitmap bitmap, long presentationTimeUs) {
        if (result != PixelCopy.SUCCESS) {
            bitmap.recycle();
            synchronized (gate) {
                pixelCopyPending = false;
            }
            recordFailure(new IllegalStateException(
                "Gate 5 PixelCopy failed with result " + result + "."));
            return;
        }

        long checksum;
        try {
            checksum = checksum(bitmap);
        } finally {
            bitmap.recycle();
        }

        boolean accepted;
        synchronized (gate) {
            pixelCopyPending = false;
            accepted = !closed && failure == null && checksums.size() < ExpectedFrames;
            if (accepted) checksums.add(checksum);
        }
        if (!accepted) return;
        sessionEvidence.recordSurfacePresentation(presentationTimeUs, System.nanoTime());
        completed.countDown();
    }

    private static long checksum(Bitmap bitmap) {
        int[] pixels = new int[bitmap.getWidth() * bitmap.getHeight()];
        bitmap.getPixels(
            pixels, 0, bitmap.getWidth(), 0, 0,
            bitmap.getWidth(), bitmap.getHeight());
        long hash = 0xcbf29ce484222325L;
        boolean nonblank = false;
        for (int pixel : pixels) {
            int rgb = pixel & 0x00ffffff;
            nonblank |= rgb != 0;
            hash ^= Integer.toUnsignedLong(rgb);
            hash *= 0x100000001b3L;
        }
        if (!nonblank) return 0;
        return hash == 0 ? 1 : hash;
    }
}
