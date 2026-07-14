package dev.beacon.android;

import java.util.concurrent.Executor;

final class MediaCodecDeviceBenchmarkRoundExecutor implements DeviceBenchmarkRoundExecutor {
    private final EncodedVideoCodecFactory codecFactory;
    private final BenchmarkVectorRepository vectors;
    private final BenchmarkPresentationSurfaceFactory surfaces;
    private final Executor finalizer;

    MediaCodecDeviceBenchmarkRoundExecutor(
        EncodedVideoCodecFactory codecFactory,
        BenchmarkVectorRepository vectors,
        BenchmarkPresentationSurfaceFactory surfaces) {
        this(codecFactory, vectors, surfaces, command -> {
            Thread thread = new Thread(command, "beacon-benchmark-finalize");
            thread.start();
        });
    }

    MediaCodecDeviceBenchmarkRoundExecutor(
        EncodedVideoCodecFactory codecFactory,
        BenchmarkVectorRepository vectors,
        BenchmarkPresentationSurfaceFactory surfaces,
        Executor finalizer) {
        if (codecFactory == null || vectors == null || surfaces == null || finalizer == null) {
            throw new IllegalArgumentException("MediaCodec benchmark dependencies are required.");
        }
        this.codecFactory = codecFactory;
        this.vectors = vectors;
        this.surfaces = surfaces;
        this.finalizer = finalizer;
    }

    @Override
    public Run start(BeaconBenchmarkHardwarePlan.DecoderRound round, Observer observer) {
        if (round == null || observer == null) {
            throw new IllegalArgumentException("Decoder benchmark round and observer are required.");
        }

        RepeatingAccessUnitVideoSampleProvider vectorSamples;
        try {
            vectorSamples = new RepeatingAccessUnitVideoSampleProvider(
                vectors.load(round.vectorId()),
                round.targetFps(),
                round.repetitionCount());
        } catch (RuntimeException failure) {
            observer.onFailure(failure);
            return () -> { };
        }

        EncodedVideoSampleProvider samples =
            new PacedEncodedVideoSampleProvider(vectorSamples);
        RoundState state = new RoundState(
            round,
            vectorSamples.expectedFrameCount(),
            observer,
            finalizer);
        try {
            BenchmarkPresentationSurface surface = surfaces.create(
                round.width(),
                round.height(),
                state);
            state.attachSurface(surface);
            EncodedVideoCodec codec = codecFactory.create(round.codec());
            state.attachCodec(codec);
            codec.configure(
                new EncodedVideoDecodeRequest(
                    round.codec(),
                    round.width(),
                    round.height(),
                    round.targetFps(),
                    samples),
                surface.surface(),
                samples,
                state);
            codec.start();
            state.markStarted();
        } catch (RuntimeException failure) {
            state.completeConfigurationFailure();
        }
        return state;
    }

    private static final class RoundState implements
        Run,
        EncodedVideoCodecObserver,
        BenchmarkPresentationSurfaceFactory.Observer {
        private final BeaconBenchmarkHardwarePlan.DecoderRound round;
        private final Observer observer;
        private final DecoderBenchmarkMeasurements measurements;
        private final Executor finalizer;
        private EncodedVideoCodec codec;
        private BenchmarkPresentationSurface surface;
        private boolean started;
        private boolean finalizing;
        private boolean finished;

        RoundState(
            BeaconBenchmarkHardwarePlan.DecoderRound round,
            int expectedFrames,
            Observer observer,
            Executor finalizer) {
            this.round = round;
            this.observer = observer;
            this.finalizer = finalizer;
            this.measurements = new DecoderBenchmarkMeasurements(round, expectedFrames);
        }

        synchronized void attachSurface(BenchmarkPresentationSurface surface) {
            this.surface = surface;
        }

        synchronized void attachCodec(EncodedVideoCodec codec) {
            this.codec = codec;
        }

        synchronized void markStarted() {
            started = true;
        }

        void completeConfigurationFailure() {
            beginFinish(false);
        }

        @Override
        public synchronized void onInputQueued(
            long frameSequence,
            long presentationTimeUs,
            long queuedAtNs) {
            if (finished) return;
            measurements.recordInput(presentationTimeUs, queuedAtNs);
        }

        @Override
        public void onOutputReleased(
            long frameSequence,
            long presentationTimeUs,
            long releasedAtNs,
            boolean rendered) {
            synchronized (this) {
                if (finished) return;
                measurements.recordOutput(presentationTimeUs, releasedAtNs, rendered);
            }
        }

        @Override
        public void onFrameRendered(
            long frameSequence,
            long presentationTimeUs,
            long renderedAtNs) { }

        @Override
        public void onFramePresented(long presentationTimeUs, long presentedAtNs) {
            synchronized (this) {
                if (finished) return;
                measurements.recordPresentation(presentationTimeUs, presentedAtNs);
            }
        }

        @Override
        public void onEndOfStream() {
            synchronized (this) {
                if (finished) return;
            }
            beginFinish(true);
        }

        @Override
        public void onError(Throwable failure) {
            synchronized (this) {
                if (finished) return;
                measurements.recordError();
            }
            beginFinish(true);
        }

        @Override
        public void onFailure(Throwable failure) {
            onError(failure);
        }

        private void beginFinish(boolean configured) {
            synchronized (this) {
                if (finished || finalizing) return;
                finalizing = true;
            }
            finalizer.execute(() -> finish(configured));
        }

        private void finish(boolean configured) {
            int cleanupErrors = cleanup();
            BeaconBenchmarkCompletionRequest.DecoderSample sample;
            synchronized (this) {
                if (finished) return;
                finished = true;
                sample = measurements.toSample(configured, cleanupErrors);
            }
            observer.onCompleted(sample);
        }

        private int cleanup() {
            EncodedVideoCodec ownedCodec;
            BenchmarkPresentationSurface ownedSurface;
            boolean stop;
            synchronized (this) {
                ownedCodec = codec;
                ownedSurface = surface;
                stop = started;
                codec = null;
                surface = null;
                started = false;
            }
            int errors = 0;
            if (ownedCodec != null) {
                try {
                    if (stop) {
                        ownedCodec.stop();
                    }
                } catch (RuntimeException failure) {
                    errors++;
                }
                try {
                    ownedCodec.release();
                } catch (RuntimeException failure) {
                    errors++;
                }
            }
            if (ownedSurface != null) {
                try {
                    ownedSurface.close();
                } catch (RuntimeException failure) {
                    errors++;
                }
            }
            return errors;
        }

        @Override
        public void cancel() {
            synchronized (this) {
                if (finished) return;
                finished = true;
            }
            cleanup();
        }
    }
}
