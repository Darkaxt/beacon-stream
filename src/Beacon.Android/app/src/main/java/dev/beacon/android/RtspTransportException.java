package dev.beacon.android;

public final class RtspTransportException extends RuntimeException {
    public RtspTransportException(String message) {
        super(message == null ? "" : message);
    }

    public RtspTransportException(String message, Throwable cause) {
        super(message == null ? "" : message, cause);
    }
}
