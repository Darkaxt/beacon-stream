package dev.beacon.android;

public final class GameStreamRtspSessionResult {
    private final boolean success;
    private final String status;
    private final String diagnostic;
    private final GameStreamRtspSessionInfo sessionInfo;

    private GameStreamRtspSessionResult(
        boolean success,
        String status,
        String diagnostic,
        GameStreamRtspSessionInfo sessionInfo) {
        this.success = success;
        this.status = status == null ? "" : status;
        this.diagnostic = diagnostic == null ? "" : diagnostic;
        this.sessionInfo = sessionInfo == null ? GameStreamRtspSessionInfo.empty() : sessionInfo;
    }

    public static GameStreamRtspSessionResult started(String status) {
        return started(status, GameStreamRtspSessionInfo.empty());
    }

    public static GameStreamRtspSessionResult started(String status, GameStreamRtspSessionInfo sessionInfo) {
        return new GameStreamRtspSessionResult(true, status, "", sessionInfo);
    }

    public static GameStreamRtspSessionResult failed(String diagnostic) {
        return new GameStreamRtspSessionResult(false, "", diagnostic, GameStreamRtspSessionInfo.empty());
    }

    public boolean success() {
        return success;
    }

    public String status() {
        return status;
    }

    public String diagnostic() {
        return diagnostic;
    }

    public GameStreamRtspSessionInfo sessionInfo() {
        return sessionInfo;
    }
}
