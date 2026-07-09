package dev.beacon.android;

public final class EncodedVideoDecodeResult {
    private final boolean success;
    private final String status;
    private final String diagnostic;

    private EncodedVideoDecodeResult(boolean success, String status, String diagnostic) {
        this.success = success;
        this.status = status == null ? "" : status;
        this.diagnostic = diagnostic == null ? "" : diagnostic;
    }

    public static EncodedVideoDecodeResult started(String status) {
        return new EncodedVideoDecodeResult(true, status, "");
    }

    public static EncodedVideoDecodeResult failed(String diagnostic) {
        return new EncodedVideoDecodeResult(false, "", diagnostic);
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
