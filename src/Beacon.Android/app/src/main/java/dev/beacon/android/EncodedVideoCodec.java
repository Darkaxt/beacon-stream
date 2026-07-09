package dev.beacon.android;

public interface EncodedVideoCodec {
    void configure(EncodedVideoStreamPlan plan, Object surface, EncodedVideoSampleProvider sampleProvider);

    void start();

    void stop();

    void release();
}
