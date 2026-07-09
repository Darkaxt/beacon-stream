package dev.beacon.android;

public interface EncodedVideoCodec {
    void configure(EncodedVideoStreamPlan plan, Object surface);

    void start();

    void stop();

    void release();
}
