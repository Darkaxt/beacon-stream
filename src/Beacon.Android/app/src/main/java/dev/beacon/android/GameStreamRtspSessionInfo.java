package dev.beacon.android;

public final class GameStreamRtspSessionInfo {
    private static final GameStreamRtspSessionInfo EMPTY = new GameStreamRtspSessionInfo(
        false,
        "",
        "",
        "",
        -1,
        -1,
        -1,
        -1,
        -1,
        -1,
        null,
        "");

    private final boolean present;
    private final String protocol;
    private final String rtspUri;
    private final String sessionId;
    private final int audioClientPort;
    private final int audioServerPort;
    private final int videoClientPort;
    private final int videoServerPort;
    private final int controlClientPort;
    private final int controlServerPort;
    private final GameStreamRtpPortLease rtpPortLease;
    private final String h264SpropParameterSets;

    private GameStreamRtspSessionInfo(
        boolean present,
        String protocol,
        String rtspUri,
        String sessionId,
        int audioClientPort,
        int audioServerPort,
        int videoClientPort,
        int videoServerPort,
        int controlClientPort,
        int controlServerPort,
        GameStreamRtpPortLease rtpPortLease,
        String h264SpropParameterSets) {
        this.present = present;
        this.protocol = protocol == null ? "" : protocol;
        this.rtspUri = rtspUri == null ? "" : rtspUri;
        this.sessionId = sessionId == null ? "" : sessionId;
        this.audioClientPort = audioClientPort;
        this.audioServerPort = audioServerPort;
        this.videoClientPort = videoClientPort;
        this.videoServerPort = videoServerPort;
        this.controlClientPort = controlClientPort;
        this.controlServerPort = controlServerPort;
        this.rtpPortLease = rtpPortLease;
        this.h264SpropParameterSets = trim(h264SpropParameterSets);
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
        return startedCore(
            protocol,
            rtspUri,
            sessionId,
            -1,
            audioServerPort,
            -1,
            videoServerPort,
            -1,
            controlServerPort,
            false);
    }

    public static GameStreamRtspSessionInfo startedWithClientPorts(
        String protocol,
        String rtspUri,
        String sessionId,
        int audioClientPort,
        int audioServerPort,
        int videoClientPort,
        int videoServerPort,
        int controlClientPort,
        int controlServerPort) {
        return startedWithClientPorts(
            protocol,
            rtspUri,
            sessionId,
            audioClientPort,
            audioServerPort,
            videoClientPort,
            videoServerPort,
            controlClientPort,
            controlServerPort,
            null);
    }

    public static GameStreamRtspSessionInfo startedWithClientPorts(
        String protocol,
        String rtspUri,
        String sessionId,
        int audioClientPort,
        int audioServerPort,
        int videoClientPort,
        int videoServerPort,
        int controlClientPort,
        int controlServerPort,
        GameStreamRtpPortLease rtpPortLease) {
        return startedCore(
            protocol,
            rtspUri,
            sessionId,
            audioClientPort,
            audioServerPort,
            videoClientPort,
            videoServerPort,
            controlClientPort,
            controlServerPort,
            true,
            rtpPortLease,
            "");
    }

    public static GameStreamRtspSessionInfo startedWithClientPorts(
        String protocol,
        String rtspUri,
        String sessionId,
        int audioClientPort,
        int audioServerPort,
        int videoClientPort,
        int videoServerPort,
        int controlClientPort,
        int controlServerPort,
        GameStreamRtpPortLease rtpPortLease,
        String h264SpropParameterSets) {
        return startedCore(
            protocol,
            rtspUri,
            sessionId,
            audioClientPort,
            audioServerPort,
            videoClientPort,
            videoServerPort,
            controlClientPort,
            controlServerPort,
            true,
            rtpPortLease,
            h264SpropParameterSets);
    }

    private static GameStreamRtspSessionInfo startedCore(
        String protocol,
        String rtspUri,
        String sessionId,
        int audioClientPort,
        int audioServerPort,
        int videoClientPort,
        int videoServerPort,
        int controlClientPort,
        int controlServerPort,
        boolean requireClientPorts) {
        return startedCore(
            protocol,
            rtspUri,
            sessionId,
            audioClientPort,
            audioServerPort,
            videoClientPort,
            videoServerPort,
            controlClientPort,
            controlServerPort,
            requireClientPorts,
            null,
            "");
    }

    private static GameStreamRtspSessionInfo startedCore(
        String protocol,
        String rtspUri,
        String sessionId,
        int audioClientPort,
        int audioServerPort,
        int videoClientPort,
        int videoServerPort,
        int controlClientPort,
        int controlServerPort,
        boolean requireClientPorts,
        GameStreamRtpPortLease rtpPortLease,
        String h264SpropParameterSets) {
        String safeProtocol = trim(protocol);
        String safeRtspUri = trim(rtspUri);
        String safeSessionId = trim(sessionId);
        boolean present = !safeProtocol.isEmpty() &&
            !safeRtspUri.isEmpty() &&
            !safeSessionId.isEmpty() &&
            audioServerPort > 0 &&
            videoServerPort > 0 &&
            controlServerPort > 0 &&
            (!requireClientPorts ||
                (audioClientPort > 0 &&
                    videoClientPort > 0 &&
                    controlClientPort > 0));
        if (!present) {
            return EMPTY;
        }

        return new GameStreamRtspSessionInfo(
            true,
            safeProtocol,
            safeRtspUri,
            safeSessionId,
            audioClientPort,
            audioServerPort,
            videoClientPort,
            videoServerPort,
            controlClientPort,
            controlServerPort,
            rtpPortLease,
            h264SpropParameterSets);
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

    public int audioClientPort() {
        return audioClientPort;
    }

    public int audioServerPort() {
        return audioServerPort;
    }

    public int videoClientPort() {
        return videoClientPort;
    }

    public int videoServerPort() {
        return videoServerPort;
    }

    public int controlClientPort() {
        return controlClientPort;
    }

    public int controlServerPort() {
        return controlServerPort;
    }

    public GameStreamRtpPortLease rtpPortLease() {
        return rtpPortLease;
    }

    public String h264SpropParameterSets() {
        return h264SpropParameterSets;
    }

    private static String trim(String value) {
        return value == null ? "" : value.trim();
    }
}
