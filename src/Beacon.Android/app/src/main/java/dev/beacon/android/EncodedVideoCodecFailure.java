package dev.beacon.android;

final class EncodedVideoCodecFailure extends RuntimeException {
    private final int platformErrorCode;

    EncodedVideoCodecFailure(int platformErrorCode, String message) {
        super(message);
        this.platformErrorCode = platformErrorCode;
    }

    EncodedVideoCodecFailure(int platformErrorCode, Throwable cause) {
        super(cause == null ? "Encoded video codec failed." : cause.getMessage(), cause);
        this.platformErrorCode = platformErrorCode;
    }

    int platformErrorCode() {
        return platformErrorCode;
    }
}
