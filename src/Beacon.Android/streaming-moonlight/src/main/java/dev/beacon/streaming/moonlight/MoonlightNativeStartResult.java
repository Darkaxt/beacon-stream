package dev.beacon.streaming.moonlight;

public final class MoonlightNativeStartResult {
    private final boolean success;
    private final int errorCode;
    private final String diagnostic;

    private MoonlightNativeStartResult(boolean success, int errorCode, String diagnostic) {
        this.success = success;
        this.errorCode = errorCode;
        this.diagnostic = diagnostic == null ? "" : diagnostic;
    }

    public static MoonlightNativeStartResult started() {
        return new MoonlightNativeStartResult(true, 0, "");
    }

    public static MoonlightNativeStartResult failed(int errorCode, String diagnostic) {
        return new MoonlightNativeStartResult(false, errorCode, diagnostic);
    }

    public boolean success() {
        return success;
    }

    public int errorCode() {
        return errorCode;
    }

    public String diagnostic() {
        return diagnostic;
    }
}
