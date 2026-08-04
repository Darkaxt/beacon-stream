package dev.beacon.android;

import java.util.concurrent.Executor;

final class BeaconVideoPipeline implements
    BeaconStreamCore.EncodedFrameSink,
    BeaconAccessUnitQueue.Observer,
    EncodedVideoCodecObserver,
    EncodedVideoSurfaceProvider.Observer,
    AutoCloseable {
    enum DecoderState {
        READY,
        AWAITING_IDR,
        FAILED
    }

    interface Observer {
        void onDecoderStateChanged(DecoderState state, int platformErrorCode);
        void onQueueDepthChanged(int queuedAccessUnits, long droppedAccessUnits);
        void onIdrRequired(long lastCompleteSequence);
        void onFrameRendered(
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs);
        void onFailure(Throwable failure);
        default void onOutputFormatChanged(EncodedVideoOutputFormat format) { }
    }

    private final Object gate = new Object();
    private final EncodedVideoSurfaceProvider surfaceProvider;
    private final BeaconAccessUnitQueue queue;
    private final SurfaceEncodedVideoDecoder decoder;
    private final Executor lifecycleExecutor;
    private final Observer observer;
    private EncodedVideoDecodeRequest request;
    private DecoderState lastState;
    private int lastPlatformErrorCode;
    private boolean decoderActive;
    private boolean recoveryScheduled;
    private boolean startedBefore;
    private boolean closed;

    BeaconVideoPipeline(
        EncodedVideoCodecFactory codecFactory,
        EncodedVideoSurfaceProvider surfaceProvider,
        int queueCapacity,
        Executor lifecycleExecutor,
        Observer observer) {
        if (codecFactory == null || surfaceProvider == null ||
            lifecycleExecutor == null || observer == null) {
            throw new IllegalArgumentException("Beacon video pipeline dependencies are required.");
        }
        this.surfaceProvider = surfaceProvider;
        this.lifecycleExecutor = lifecycleExecutor;
        this.observer = observer;
        queue = new BeaconAccessUnitQueue(queueCapacity, this);
        decoder = new SurfaceEncodedVideoDecoder(codecFactory, surfaceProvider, this);
        surfaceProvider.setObserver(this);
    }

    void start(String codec, int width, int height, int fps) {
        start(new EncodedVideoDecodeRequest(
            codec,
            width,
            height,
            fps,
            queue));
    }

    void start(BeaconStreamSession.SelectedVideo video) {
        if (video == null) {
            throw new IllegalArgumentException("Selected video is required.");
        }
        start(new EncodedVideoDecodeRequest(
            video.codec(),
            video.width(),
            video.height(),
            framesPerSecond(video),
            video.profile(),
            video.bitDepth(),
            video.dynamicRange(),
            video.colorPrimaries(),
            video.transferFunction(),
            video.matrixCoefficients(),
            video.colorRange(),
            video.hdrStaticInfo(),
            video.hdrStaticInfoInBitstream(),
            queue));
    }

    private void start(EncodedVideoDecodeRequest replacement) {
        boolean resetQueue;
        synchronized (gate) {
            if (closed) {
                throw new IllegalStateException("Beacon video pipeline is closed.");
            }
            request = replacement;
            decoderActive = false;
            recoveryScheduled = false;
            lastState = null;
            lastPlatformErrorCode = 0;
            resetQueue = startedBefore;
            startedBefore = true;
        }
        if (resetQueue) queue.resetForDecoder();
        executeLifecycle(() -> startRequestedDecoder(replacement));
    }

    private static int framesPerSecond(BeaconStreamSession.SelectedVideo video) {
        long numerator = video.framesPerSecondNumerator();
        long denominator = video.framesPerSecondDenominator();
        long rounded = (numerator + denominator / 2) / denominator;
        if (rounded <= 0 || rounded > Integer.MAX_VALUE) {
            throw new IllegalArgumentException("Selected video frame rate is invalid.");
        }
        return (int) rounded;
    }

    void stop() {
        synchronized (gate) {
            if (closed) return;
            request = null;
            decoderActive = false;
            recoveryScheduled = false;
            lastState = null;
            lastPlatformErrorCode = 0;
        }
        queue.resetForDecoder();
        executeLifecycle(this::stopDecoderSafely);
    }

    @Override
    public void onFrame(BeaconStreamCore.EncodedFrame frame) {
        synchronized (gate) {
            if (closed || request == null) return;
        }
        queue.onFrame(frame);
    }

    @Override
    public void onQueueDepthChanged(int queuedAccessUnits, long droppedAccessUnits) {
        synchronized (gate) {
            if (closed || request == null) return;
        }
        observer.onQueueDepthChanged(queuedAccessUnits, droppedAccessUnits);
    }

    @Override
    public void onIdrRequired(long lastCompleteSequence) {
        synchronized (gate) {
            if (closed || request == null) return;
        }
        observer.onIdrRequired(lastCompleteSequence);
    }

    @Override
    public void onAwaitingIdrChanged(boolean awaitingIdr) {
        publishState(awaitingIdr ? DecoderState.AWAITING_IDR : DecoderState.READY, 0);
    }

    @Override
    public void onInputQueued(
        long frameSequence,
        long presentationTimeUs,
        long queuedAtNs) { }

    @Override
    public void onOutputReleased(
        long frameSequence,
        long presentationTimeUs,
        long releasedAtNs,
        boolean rendered) { }

    @Override
    public void onFrameRendered(
        long frameSequence,
        long presentationTimeUs,
        long renderedAtNs) {
        synchronized (gate) {
            if (closed || request == null || !decoderActive) return;
        }
        observer.onFrameRendered(
            frameSequence,
            presentationTimeUs,
            renderedAtNs / 1_000);
    }

    @Override
    public void onOutputFormatChanged(EncodedVideoOutputFormat format) {
        synchronized (gate) {
            if (closed || request == null || !decoderActive) return;
        }
        observer.onOutputFormatChanged(format);
    }

    @Override
    public void onEndOfStream() { }

    @Override
    public void onError(Throwable failure) {
        if (failure instanceof EncodedVideoOutputFormatMismatch) {
            failClosed(failure);
            return;
        }
        int platformErrorCode = failure instanceof EncodedVideoCodecFailure codecFailure
            ? codecFailure.platformErrorCode()
            : 0;
        synchronized (gate) {
            if (closed || request == null || recoveryScheduled) return;
            recoveryScheduled = true;
        }
        publishState(DecoderState.FAILED, platformErrorCode);
        try {
            lifecycleExecutor.execute(this::recoverDecoder);
        } catch (RuntimeException schedulingFailure) {
            synchronized (gate) {
                recoveryScheduled = false;
            }
            observer.onFailure(schedulingFailure);
        }
    }

    private void failClosed(Throwable failure) {
        synchronized (gate) {
            if (closed || request == null) return;
        }
        publishState(DecoderState.FAILED, 0);
        synchronized (gate) {
            request = null;
            decoderActive = false;
            recoveryScheduled = false;
        }
        queue.resetForDecoder();
        executeLifecycle(this::stopDecoderSafely);
        observer.onFailure(failure);
    }

    @Override
    public void onSurfaceAvailable() {
        executeLifecycle(this::restartForAvailableSurface);
    }

    @Override
    public void onSurfaceDestroyed() {
        executeLifecycle(this::stopForDestroyedSurface);
    }

    @Override
    public void close() {
        synchronized (gate) {
            if (closed) return;
            closed = true;
            request = null;
            decoderActive = false;
            recoveryScheduled = false;
        }
        surfaceProvider.setObserver(EncodedVideoSurfaceProvider.Observer.noOp());
        queue.close();
        executeLifecycle(this::stopDecoderSafely);
    }

    private void startRequestedDecoder(EncodedVideoDecodeRequest requested) {
        synchronized (gate) {
            if (closed || request != requested) return;
        }
        startDecoder(requested);
    }

    private EncodedVideoDecodeResult startDecoder(EncodedVideoDecodeRequest requested) {
        EncodedVideoDecodeResult result = decoder.start(requested);
        synchronized (gate) {
            if (closed || request != requested) {
                if (result.success()) {
                    try {
                        decoder.stop();
                    } catch (RuntimeException failure) {
                        observer.onFailure(failure);
                    }
                }
                return result;
            }
            decoderActive = result.success();
        }
        if (result.success()) {
            publishState(DecoderState.AWAITING_IDR, 0);
        } else {
            publishState(DecoderState.FAILED, 0);
            observer.onFailure(new IllegalStateException(result.diagnostic()));
        }
        return result;
    }

    private void recoverDecoder() {
        EncodedVideoDecodeRequest activeRequest;
        synchronized (gate) {
            if (closed || request == null) {
                recoveryScheduled = false;
                return;
            }
            activeRequest = request;
            decoderActive = false;
        }
        queue.resetForDecoder();
        stopDecoderSafely();
        synchronized (gate) {
            recoveryScheduled = false;
            if (closed || request != activeRequest) return;
        }
        if (surfaceProvider.currentSurface() != null) startDecoder(activeRequest);
    }

    private void stopForDestroyedSurface() {
        synchronized (gate) {
            if (closed || request == null || !decoderActive) return;
            decoderActive = false;
        }
        queue.resetForDecoder();
        stopDecoderSafely();
    }

    private void restartForAvailableSurface() {
        EncodedVideoDecodeRequest activeRequest;
        synchronized (gate) {
            if (closed || request == null || decoderActive || recoveryScheduled) return;
            activeRequest = request;
        }
        startDecoder(activeRequest);
    }

    private void executeLifecycle(Runnable action) {
        try {
            lifecycleExecutor.execute(action);
        } catch (RuntimeException failure) {
            observer.onFailure(failure);
        }
    }

    private void stopDecoderSafely() {
        try {
            decoder.stop();
        } catch (RuntimeException failure) {
            observer.onFailure(failure);
        }
    }

    private void publishState(DecoderState state, int platformErrorCode) {
        synchronized (gate) {
            if (closed || request == null ||
                (lastState == state && lastPlatformErrorCode == platformErrorCode)) {
                return;
            }
            lastState = state;
            lastPlatformErrorCode = platformErrorCode;
        }
        observer.onDecoderStateChanged(state, platformErrorCode);
    }
}
