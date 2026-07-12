package dev.beacon.android;

public interface EncodedVideoSampleProvider {
    EncodedVideoSample nextSample();

    default int maxSampleBytes() {
        return 0;
    }

    static EncodedVideoSampleProvider endOfStreamOnly() {
        return EncodedVideoSample::eos;
    }
}
