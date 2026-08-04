package dev.beacon.android;

public interface EncodedVideoCodec {
    void configure(EncodedVideoDecodeRequest request, Object surface, EncodedVideoSampleProvider sampleProvider);

    default void configure(
        EncodedVideoDecodeRequest request,
        Object surface,
        EncodedVideoSampleProvider sampleProvider,
        EncodedVideoCodecObserver observer) {
        configure(request, surface, sampleProvider);
    }

    void start();

    void stop();

    void release();
}
