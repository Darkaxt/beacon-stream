package dev.beacon.android;

public final class BeaconHttpResponse {
    private final int statusCode;
    private final String body;

    public BeaconHttpResponse(int statusCode, String body) {
        this.statusCode = statusCode;
        this.body = body == null ? "" : body;
    }

    public int statusCode() {
        return statusCode;
    }

    public String body() {
        return body;
    }

    public boolean isSuccess() {
        return statusCode >= 200 && statusCode <= 299;
    }
}
