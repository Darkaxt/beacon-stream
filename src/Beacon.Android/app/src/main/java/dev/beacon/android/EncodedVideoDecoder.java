package dev.beacon.android;

public interface EncodedVideoDecoder {
    EncodedVideoDecodeResult start(EncodedVideoDecodeRequest request);

    void stop();
}
