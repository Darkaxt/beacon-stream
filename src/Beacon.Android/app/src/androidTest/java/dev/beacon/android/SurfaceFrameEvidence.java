package dev.beacon.android;

import android.graphics.Bitmap;
import android.os.Handler;
import android.os.Looper;
import android.view.PixelCopy;
import android.view.SurfaceView;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;

final class SurfaceFrameEvidence implements BeaconVideoPipeline.Observer {
    private static final int EvidenceWidth = 64;
    private static final int EvidenceHeight = 36;

    private final Object gate = new Object();
    private final SurfaceView surfaceView;
    private final BeaconVideoPipeline.Observer feedback;
    private final int expectedFrames;
    private final Handler pixelCopyHandler = new Handler(Looper.getMainLooper());
    private final CountDownLatch completed = new CountDownLatch(1);
    private final List<Long> renderedSequences = new ArrayList<>();
    private final List<Long> pixelChecksums = new ArrayList<>();
    private Throwable failure;
    private boolean pixelCopyPending;

    SurfaceFrameEvidence(
        SurfaceView surfaceView,
        BeaconVideoPipeline.Observer feedback,
        int expectedFrames) {
        if (surfaceView == null || feedback == null || expectedFrames <= 0) {
            throw new IllegalArgumentException(
                "Surface frame evidence dependencies are required.");
        }
        this.surfaceView = surfaceView;
        this.feedback = feedback;
        this.expectedFrames = expectedFrames;
    }

    @Override
    public void onDecoderStateChanged(
        BeaconVideoPipeline.DecoderState state,
        int platformErrorCode) {
        forward(() -> feedback.onDecoderStateChanged(state, platformErrorCode));
    }

    @Override
    public void onQueueDepthChanged(
        int queuedAccessUnits,
        long droppedAccessUnits) {
        forward(() -> feedback.onQueueDepthChanged(
            queuedAccessUnits, droppedAccessUnits));
    }

    @Override
    public void onIdrRequired(long lastCompleteSequence) {
        forward(() -> feedback.onIdrRequired(lastCompleteSequence));
    }

    @Override
    public void onFrameRendered(
        long frameSequence,
        long presentationTimeUs,
        long renderedAtUs) {
        synchronized (gate) {
            if (failure != null || renderedSequences.size() >= expectedFrames) return;
            if (pixelCopyPending) {
                recordFailure(new IllegalStateException(
                    "A rendered frame arrived while PixelCopy was still pending."));
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
                result -> finishPixelCopy(
                    result,
                    bitmap,
                    frameSequence,
                    presentationTimeUs,
                    renderedAtUs),
                pixelCopyHandler);
        } catch (RuntimeException failure) {
            bitmap.recycle();
            synchronized (gate) {
                pixelCopyPending = false;
            }
            recordFailure(failure);
        }
    }

    @Override
    public void onFailure(Throwable observedFailure) {
        recordFailure(observedFailure);
    }

    void awaitCompletion() throws InterruptedException {
        completed.await();
        Throwable observedFailure;
        int observedFrames;
        synchronized (gate) {
            observedFailure = failure;
            observedFrames = renderedSequences.size();
        }
        if (observedFailure != null) {
            throw new AssertionError(
                "Hosted SurfaceView evidence failed.", observedFailure);
        }
        if (observedFrames != expectedFrames) {
            throw new AssertionError(
                "Expected " + expectedFrames +
                    " rendered frames but observed " + observedFrames + ".");
        }
    }

    List<Long> renderedSequences() {
        synchronized (gate) {
            return List.copyOf(renderedSequences);
        }
    }

    List<Long> pixelChecksums() {
        synchronized (gate) {
            return List.copyOf(pixelChecksums);
        }
    }

    void recordFailure(Throwable observedFailure) {
        Throwable normalized = observedFailure == null
            ? new IllegalStateException("Surface frame evidence failed without a cause.")
            : observedFailure;
        synchronized (gate) {
            if (failure != null) return;
            failure = normalized;
            pixelCopyPending = false;
        }
        completed.countDown();
    }

    private void finishPixelCopy(
        int result,
        Bitmap bitmap,
        long frameSequence,
        long presentationTimeUs,
        long renderedAtUs) {
        if (result != PixelCopy.SUCCESS) {
            bitmap.recycle();
            synchronized (gate) {
                pixelCopyPending = false;
            }
            recordFailure(new IllegalStateException(
                "PixelCopy failed with result " + result + "."));
            return;
        }

        long checksum;
        try {
            checksum = checksum(bitmap);
        } finally {
            bitmap.recycle();
        }

        boolean evidenceComplete;
        synchronized (gate) {
            pixelCopyPending = false;
            if (failure != null) return;
            long expectedSequence = renderedSequences.size() + 1L;
            if (frameSequence != expectedSequence) {
                recordFailure(new IllegalStateException(
                    "Expected rendered frame " + expectedSequence +
                        " but received " + frameSequence + "."));
                return;
            }
            renderedSequences.add(frameSequence);
            pixelChecksums.add(checksum);
            evidenceComplete = renderedSequences.size() == expectedFrames;
        }

        try {
            feedback.onFrameRendered(
                frameSequence, presentationTimeUs, renderedAtUs);
        } catch (RuntimeException | Error sendFailure) {
            recordFailure(sendFailure);
            return;
        }
        if (evidenceComplete) completed.countDown();
    }

    private void forward(Runnable callback) {
        synchronized (gate) {
            if (failure != null) return;
        }
        try {
            callback.run();
        } catch (RuntimeException | Error callbackFailure) {
            recordFailure(callbackFailure);
        }
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
