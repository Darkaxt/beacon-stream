package dev.beacon.android;

import java.io.IOException;

public interface BeaconHttpTransport {
    BeaconHttpResponse send(String method, String path, String body) throws IOException;
}
