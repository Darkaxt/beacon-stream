package dev.beacon.android;

final class MediaCodecDeviceBenchmarkRoundExecutor implements DeviceBenchmarkRoundExecutor {
    private final EncodedVideoCodecFactory codecFactory;
    private final BenchmarkVectorRepository vectors;
    private final BenchmarkPresentationSurfaceFactory surfaces;

    MediaCodecDeviceBenchmarkRoundExecutor(
        EncodedVideoCodecFactory codecFactory,
        BenchmarkVectorRepository vectors,
        BenchmarkPresentationSurfaceFactory surfaces) {
        if (codecFactory == null || vectors == null || surfaces == null) {
            throw new IllegalArgumentException("MediaCodec benchmark dependencies are required.");
        }
        this.codecFactory = codecFactory;
        this.vectors = vectors;
        this.surfaces = surfaces;
    }

    @Override
    public Run start(BeaconBenchmarkHardwarePlan.DecoderRound round, Observer observer) {
        if (round == null || observer == null) {
            throw new IllegalArgumentException("Decoder benchmark round and observer are required.");
        }

        RepeatingAnnexBVideoSampleProvider samples;
        try {
            samples = new RepeatingAnnexBVideoSampleProvider(
                vectors.load(round.vectorId()),
                round.targetFps(),
                round.repetitionCount());
        } catch (RuntimeException failure) {
            observer.onFailure(failure);
            return () -> { };
        }

        RoundState state = new RoundState(round, samples.expectedFrameCount(), observer);
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
        private EncodedVideoCodec codec;
        private BenchmarkPresentationSurface surface;
        private boolean started;
        private boolean finished;
        private boolean eos;

        RoundState(
            BeaconBenchmarkHardwarePlan.DecoderRound round,
            int expectedFrames,
            Observer observer) {
            this.round = round;
            this.observer = observer;
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
            finish(false);
        }

        @Override
        public synchronized void onInputQueued(long presentationTimeUs, long queuedAtNs) {
            if (finished) return;
            measurements.recordInput(presentationTimeUs, queuedAtNs);
        }

        @Override
        public void onOutputReleased(
            long presentationTimeUs,
            long releasedAtNs,
            boolean rendered) {
            synchronized (this) {
                if (finished) return;
                measurements.recordOutput(presentationTimeUs, releasedAtNs, rendered);
            }
            finishIfDrained();
        }

        @Override
        public void onFramePresented(long presentationTimeUs, long presentedAtNs) {
            synchronized (this) {
                if (finished) return;
                measurements.recordPresentation(presentationTimeUs, presentedAtNs);
            }
            finishIfDrained();
        }

        @Override
        public void onEndOfStream() {
            synchronized (this) {
                if (finished) return;
                eos = true;
            }
            finishIfDrained();
        }

        @Override
        public void onError(Throwable failure) {
            synchronized (this) {
                if (finished) return;
                measurements.recordError();
                eos = true;
            }
            finish(true);
        }

        @Override
        public void onFailure(Throwable failure) {
            onError(failure);
        }

        private void finishIfDrained() {
            boolean ready;
            synchronized (this) {
                ready = eos && measurements.presentationDrained();
            }
            if (ready) finish(true);
        }

        private void finish(boolean configured) {
            synchronized (this) {
                if (finished) return;
                finished = true;
            }
            int cleanupErrors = cleanup();
            BeaconBenchmarkCompletionRequest.DecoderSample sample;
            synchronized (this) {
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
