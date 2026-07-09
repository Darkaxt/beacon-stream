package dev.beacon.android;

import java.io.IOException;

public interface RtpDatagramSocketFactory {
    RtpDatagramSocket bind(int localPort) throws IOException;
}
