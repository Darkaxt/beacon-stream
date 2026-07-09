package dev.beacon.android;

public interface GameStreamRtpPacketSourceFactory {
    RtpPacketSource create(GameStreamEndpointPlan plan, GameStreamRtspSessionInfo sessionInfo);
}
