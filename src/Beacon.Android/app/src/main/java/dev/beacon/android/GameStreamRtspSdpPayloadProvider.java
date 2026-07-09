package dev.beacon.android;

public interface GameStreamRtspSdpPayloadProvider {
    String createSdpPayload(
        GameStreamEndpointPlan plan,
        String sessionId,
        int audioPort,
        int videoPort,
        int controlPort);

    static GameStreamRtspSdpPayloadProvider diagnostic() {
        return new GameStreamRtspSdpPayloadProvider() {
            @Override
            public String createSdpPayload(
                GameStreamEndpointPlan plan,
                String sessionId,
                int audioPort,
                int videoPort,
                int controlPort) {
                return "v=0\r\n" +
                    "o=beacon 0 0 IN IP4 127.0.0.1\r\n" +
                    "s=Beacon GameStream\r\n" +
                    "t=0 0\r\n" +
                    "a=x-beacon-session:" + sessionId + "\r\n" +
                    "a=x-beacon-audio-port:" + audioPort + "\r\n" +
                    "a=x-beacon-video-port:" + videoPort + "\r\n" +
                    "a=x-beacon-control-port:" + controlPort + "\r\n";
            }
        };
    }
}
