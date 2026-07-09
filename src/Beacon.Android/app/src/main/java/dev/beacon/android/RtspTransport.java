package dev.beacon.android;

public interface RtspTransport {
    RtspResponse transact(RtspRequest request);
}
