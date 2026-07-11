package dev.beacon.android;

import java.io.IOException;
import java.util.Map;

public interface BeaconHttpTransport {
    BeaconHttpResponse send(String method, String path, String body) throws IOException;

    default BeaconHttpResponse send(
        String method,
        String path,
        String body,
        Map<String, String> headers) throws IOException {
        return send(method, path, body);
    }
}
