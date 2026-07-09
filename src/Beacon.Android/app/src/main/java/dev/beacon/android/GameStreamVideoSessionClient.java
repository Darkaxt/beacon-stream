package dev.beacon.android;

public interface GameStreamVideoSessionClient {
    NativeStreamStartResult start(GameStreamEndpointPlan plan, GameStreamRtspSessionInfo sessionInfo);

    void stop();
}
