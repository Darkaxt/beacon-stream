package dev.beacon.android;

public final class NativeStreamStartResult {
    private final boolean success;
    private final String status;
    private final String diagnostic;

    private NativeStreamStartResult(boolean success, String status, String diagnostic) {
        this.success = success;
        this.status = status == null ? "" : status;
        this.diagnostic = diagnostic == null ? "" : diagnostic;
    }

    public static NativeStreamStartResult started(String status) {
        return new NativeStreamStartResult(true, status, "");
    }

    public static NativeStreamStartResult unsupported(String diagnostic) {
        return new NativeStreamStartResult(false, "", diagnostic);
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
