package dev.beacon.android;

import java.io.IOException;

public interface RtpDatagramSocket {
    int receive(byte[] buffer) throws IOException;

    int localPort();

    void close();
}
