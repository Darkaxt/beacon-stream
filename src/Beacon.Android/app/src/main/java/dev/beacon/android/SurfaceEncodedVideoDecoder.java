package dev.beacon.android;

public final class SurfaceEncodedVideoDecoder {
    private final EncodedVideoCodecFactory codecFactory;
    private final EncodedVideoSurfaceProvider surfaceProvider;
    private final EncodedVideoCodecObserver observer;
    private EncodedVideoCodec activeCodec;
    private long observerGeneration;

    public SurfaceEncodedVideoDecoder(
        EncodedVideoCodecFactory codecFactory,
        EncodedVideoSurfaceProvider surfaceProvider) {
        this(codecFactory, surfaceProvider, EncodedVideoCodecObserver.noOp());
    }

    SurfaceEncodedVideoDecoder(
        EncodedVideoCodecFactory codecFactory,
        EncodedVideoSurfaceProvider surfaceProvider,
        EncodedVideoCodecObserver observer) {
        if (codecFactory == null) {
            throw new IllegalArgumentException("Encoded video codec factory is required.");
        }

        if (surfaceProvider == null) {
            throw new IllegalArgumentException("Encoded video surface provider is required.");
        }
        if (observer == null) {
            throw new IllegalArgumentException("Encoded video codec observer is required.");
        }

        this.codecFactory = codecFactory;
        this.surfaceProvider = surfaceProvider;
        this.observer = observer;
    }

    public synchronized EncodedVideoDecodeResult start(EncodedVideoDecodeRequest request) {
        try {
            stopActiveCodec();
        } catch (RuntimeException failure) {
            return EncodedVideoDecodeResult.failed(diagnostic(failure));
        }

        Object surface = surfaceProvider.currentSurface();
        if (surface == null) {
            return EncodedVideoDecodeResult.failed("Encoded video surface is not ready.");
        }

        EncodedVideoSampleProvider sampleProvider = request.sampleProvider();

        EncodedVideoCodec codec = null;
        long generation = ++observerGeneration;
        try {
            codec = codecFactory.create(request.codec());
            codec.configure(
                request,
                surface,
                sampleProvider,
                new GenerationObserver(generation));
            codec.start();
            activeCodec = codec;
            return EncodedVideoDecodeResult.started(
                "MediaCodec decoder configured. codec=" +
                    request.codec() + " " + request.width() + "x" + request.height() + "@" + request.fps());
        } catch (RuntimeException ex) {
            if (observerGeneration == generation) observerGeneration++;
            if (codec != null) {
                try {
                    codec.release();
                } catch (RuntimeException releaseFailure) {
                    ex.addSuppressed(releaseFailure);
                }
            }

            return EncodedVideoDecodeResult.failed(diagnostic(ex));
        }
    }

    public synchronized void stop() {
        stopActiveCodec();
    }

    private void stopActiveCodec() {
        observerGeneration++;
        EncodedVideoCodec owned = activeCodec;
        activeCodec = null;
        if (owned == null) return;
        RuntimeException stopFailure = null;
        try {
            owned.stop();
        } catch (RuntimeException failure) {
            stopFailure = failure;
        } finally {
            try {
                owned.release();
            } catch (RuntimeException releaseFailure) {
                if (stopFailure == null) throw releaseFailure;
                stopFailure.addSuppressed(releaseFailure);
            }
        }
        if (stopFailure != null) throw stopFailure;
    }

    private static String diagnostic(RuntimeException failure) {
        return failure.getMessage() == null
            ? failure.getClass().getSimpleName()
            : failure.getMessage();
    }

    private synchronized boolean isCurrentObserver(long generation) {
        return observerGeneration == generation;
    }

    private final class GenerationObserver implements EncodedVideoCodecObserver {
        private final long generation;

        GenerationObserver(long generation) {
            this.generation = generation;
        }

        @Override
        public void onInputQueued(
            long frameSequence,
            long presentationTimeUs,
            long queuedAtNs) {
            if (isCurrentObserver(generation)) {
                observer.onInputQueued(frameSequence, presentationTimeUs, queuedAtNs);
            }
        }

        @Override
        public void onOutputReleased(
            long frameSequence,
            long presentationTimeUs,
            long releasedAtNs,
            boolean rendered) {
            if (isCurrentObserver(generation)) {
                observer.onOutputReleased(
                    frameSequence, presentationTimeUs, releasedAtNs, rendered);
            }
        }

        @Override
        public void onFrameRendered(
            long frameSequence,
            long presentationTimeUs,
            long renderedAtNs) {
            if (isCurrentObserver(generation)) {
                observer.onFrameRendered(
                    frameSequence, presentationTimeUs, renderedAtNs);
            }
        }

        @Override
        public void onEndOfStream() {
            if (isCurrentObserver(generation)) observer.onEndOfStream();
        }

        @Override
        public void onOutputFormatChanged(EncodedVideoOutputFormat format) {
            if (isCurrentObserver(generation)) observer.onOutputFormatChanged(format);
        }

        @Override
        public void onError(Throwable failure) {
            if (isCurrentObserver(generation)) observer.onError(failure);
        }
    }
}
