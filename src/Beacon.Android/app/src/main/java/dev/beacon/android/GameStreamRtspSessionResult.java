package dev.beacon.android;

public final class GameStreamRtspSessionResult {
    private final boolean success;
    private final String status;
    private final String diagnostic;

    private GameStreamRtspSessionResult(boolean success, String status, String diagnostic) {
        this.success = success;
        this.status = status == null ? "" : status;
        this.diagnostic = diagnostic == null ? "" : diagnostic;
    }

    public static GameStreamRtspSessionResult started(String status) {
        return new GameStreamRtspSessionResult(true, status, "");
    }

    public static GameStreamRtspSessionResult failed(String diagnostic) {
        return new GameStreamRtspSessionResult(false, "", diagnostic);
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
}
