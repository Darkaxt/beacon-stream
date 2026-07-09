package dev.beacon.android;

public interface EncodedVideoSampleProviderFactory {
    EncodedVideoSampleProvider create(EncodedVideoStreamPlan plan);

    static EncodedVideoSampleProviderFactory empty() {
        return plan -> EncodedVideoSampleProvider.endOfStreamOnly();
    }
}
