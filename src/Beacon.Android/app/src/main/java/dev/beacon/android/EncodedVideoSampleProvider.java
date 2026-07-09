package dev.beacon.android;

public interface EncodedVideoSampleProvider {
    EncodedVideoSample nextSample();

    static EncodedVideoSampleProvider endOfStreamOnly() {
        return EncodedVideoSample::eos;
    }
}
