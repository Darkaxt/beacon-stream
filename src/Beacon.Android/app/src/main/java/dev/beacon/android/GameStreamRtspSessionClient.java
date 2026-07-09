package dev.beacon.android;

public interface GameStreamRtspSessionClient {
    GameStreamRtspSessionResult start(GameStreamEndpointPlan plan);

    default void stop() {
    }

    static GameStreamRtspSessionClient notConfigured() {
        return plan -> GameStreamRtspSessionResult.failed(
            "Native GameStream RTSP transport is not configured yet. protocol=" +
                plan.protocol() +
                " rtsp=" +
                plan.rtspUri());
    }
}
