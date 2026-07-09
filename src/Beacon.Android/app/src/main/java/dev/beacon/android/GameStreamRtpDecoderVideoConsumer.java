package dev.beacon.android;

public final class GameStreamRtpDecoderVideoConsumer implements GameStreamRtpVideoConsumer {
    private final EncodedVideoCodecFactory codecFactory;
    private final EncodedVideoSurfaceProvider surfaceProvider;
    private EncodedVideoDecoder activeDecoder;

    public GameStreamRtpDecoderVideoConsumer(
        EncodedVideoCodecFactory codecFactory,
        EncodedVideoSurfaceProvider surfaceProvider) {
        if (codecFactory == null) {
            throw new IllegalArgumentException("Encoded video codec factory is required.");
        }

        if (surfaceProvider == null) {
            throw new IllegalArgumentException("Encoded video surface provider is required.");
        }

        this.codecFactory = codecFactory;
        this.surfaceProvider = surfaceProvider;
    }

    @Override
    public synchronized NativeStreamStartResult start(
        GameStreamEndpointPlan plan,
        GameStreamRtspSessionInfo sessionInfo,
        EncodedVideoSampleProvider sampleProvider) {
        stop();

        EncodedVideoStreamPlan videoPlan = EncodedVideoStreamPlan.fromGameStreamRtp(plan);
        if (!videoPlan.complete()) {
            return NativeStreamStartResult.unsupported(videoPlan.diagnostic());
        }

        EncodedVideoDecoder decoder = new SurfaceEncodedVideoDecoder(
            codecFactory,
            surfaceProvider,
            ignored -> sampleProvider);
        EncodedVideoDecodeResult result = decoder.start(new EncodedVideoDecodeRequest(videoPlan));
        if (!result.success()) {
            decoder.stop();
            return NativeStreamStartResult.unsupported(result.diagnostic());
        }

        activeDecoder = decoder;
        return NativeStreamStartResult.started(
            result.status(),
            NativeStreamPresentation.encodedVideo(
                videoPlan.videoUri(),
                videoPlan.codec(),
                videoPlan.width(),
                videoPlan.height(),
                videoPlan.fps()));
    }

    @Override
    public synchronized void stop() {
        if (activeDecoder == null) {
            return;
        }

        try {
            activeDecoder.stop();
        } finally {
            activeDecoder = null;
        }
    }
}
