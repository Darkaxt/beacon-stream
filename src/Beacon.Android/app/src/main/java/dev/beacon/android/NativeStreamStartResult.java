package dev.beacon.android;

public final class NativeStreamStartResult {
    private final boolean success;
    private final String status;
    private final String diagnostic;
    private final NativeStreamPresentation presentation;

    private NativeStreamStartResult(
        boolean success,
        String status,
        String diagnostic,
        NativeStreamPresentation presentation) {
        this.success = success;
        this.status = status == null ? "" : status;
        this.diagnostic = diagnostic == null ? "" : diagnostic;
        this.presentation = presentation == null ? NativeStreamPresentation.none() : presentation;
    }

    public static NativeStreamStartResult started(String status) {
        return started(status, NativeStreamPresentation.none());
    }

    public static NativeStreamStartResult started(String status, NativeStreamPresentation presentation) {
        return new NativeStreamStartResult(true, status, "", presentation);
    }

    public static NativeStreamStartResult unsupported(String diagnostic) {
        return new NativeStreamStartResult(false, "", diagnostic, NativeStreamPresentation.none());
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

    public NativeStreamPresentation presentation() {
        return presentation;
    }
}
