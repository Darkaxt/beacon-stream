package dev.beacon.android;

public interface GameStreamRtpVideoConsumer {
    NativeStreamStartResult start(
        GameStreamEndpointPlan plan,
        GameStreamRtspSessionInfo sessionInfo,
        EncodedVideoSampleProvider sampleProvider);
}
