package dev.beacon.android;

public final class SurfaceEncodedVideoDecoder implements EncodedVideoDecoder {
    private final EncodedVideoCodecFactory codecFactory;
    private final EncodedVideoSurfaceProvider surfaceProvider;
    private EncodedVideoCodec activeCodec;

    public SurfaceEncodedVideoDecoder(
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
    public EncodedVideoDecodeResult start(EncodedVideoDecodeRequest request) {
        Object surface = surfaceProvider.currentSurface();
        if (surface == null) {
            return EncodedVideoDecodeResult.failed("Encoded video surface is not ready.");
        }

        EncodedVideoSampleProvider sampleProvider = request.sampleProvider();

        EncodedVideoCodec codec = null;
        try {
            codec = codecFactory.create(request.codec());
            codec.configure(request, surface, sampleProvider);
            codec.start();
            activeCodec = codec;
            return EncodedVideoDecodeResult.started(
                "MediaCodec decoder configured. codec=" +
                    request.codec() + " " + request.width() + "x" + request.height() + "@" + request.fps());
        } catch (RuntimeException ex) {
            if (codec != null) {
                codec.release();
            }

            return EncodedVideoDecodeResult.failed(ex.getMessage() == null ? ex.getClass().getSimpleName() : ex.getMessage());
        }
    }

    @Override
    public void stop() {
        if (activeCodec == null) {
            return;
        }

        try {
            activeCodec.stop();
        } finally {
            activeCodec.release();
            activeCodec = null;
        }
    }
}
