package dev.beacon.android;

public interface EncodedVideoCodec {
    void configure(EncodedVideoDecodeRequest request, Object surface, EncodedVideoSampleProvider sampleProvider);

    void start();

    void stop();

    void release();
}
