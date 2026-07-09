package dev.beacon.android;

import java.io.IOException;

public interface RtspSocketConnector {
    RtspSocketHandle open(String host, int port) throws IOException;
}
