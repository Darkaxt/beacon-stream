package dev.beacon.android;

public final class GameStreamRtspSessionInfo {
    private static final GameStreamRtspSessionInfo EMPTY = new GameStreamRtspSessionInfo(
        false,
        "",
        "",
        "",
        -1,
        -1,
        -1);

    private final boolean present;
    private final String protocol;
    private final String rtspUri;
    private final String sessionId;
    private final int audioServerPort;
    private final int videoServerPort;
    private final int controlServerPort;

    private GameStreamRtspSessionInfo(
        boolean present,
        String protocol,
        String rtspUri,
        String sessionId,
        int audioServerPort,
        int videoServerPort,
        int controlServerPort) {
        this.present = present;
        this.protocol = protocol == null ? "" : protocol;
        this.rtspUri = rtspUri == null ? "" : rtspUri;
        this.sessionId = sessionId == null ? "" : sessionId;
        this.audioServerPort = audioServerPort;
        this.videoServerPort = videoServerPort;
        this.controlServerPort = controlServerPort;
    }

    public static GameStreamRtspSessionInfo empty() {
        return EMPTY;
    }

    public static GameStreamRtspSessionInfo started(
        String protocol,
        String rtspUri,
        String sessionId,
        int audioServerPort,
        int videoServerPort,
        int controlServerPort) {
        String safeProtocol = trim(protocol);
        String safeRtspUri = trim(rtspUri);
        String safeSessionId = trim(sessionId);
        boolean present = !safeProtocol.isEmpty() &&
            !safeRtspUri.isEmpty() &&
            !safeSessionId.isEmpty() &&
            audioServerPort > 0 &&
            videoServerPort > 0 &&
            controlServerPort > 0;
        if (!present) {
            return EMPTY;
        }

        return new GameStreamRtspSessionInfo(
            true,
            safeProtocol,
            safeRtspUri,
            safeSessionId,
            audioServerPort,
            videoServerPort,
            controlServerPort);
    }

    public boolean present() {
        return present;
    }

    public String protocol() {
        return protocol;
    }

    public String rtspUri() {
        return rtspUri;
    }

    public String sessionId() {
        return sessionId;
    }

    public int audioServerPort() {
        return audioServerPort;
    }

    public int videoServerPort() {
        return videoServerPort;
    }

    public int controlServerPort() {
        return controlServerPort;
    }

    private static String trim(String value) {
        return value == null ? "" : value.trim();
    }
}
