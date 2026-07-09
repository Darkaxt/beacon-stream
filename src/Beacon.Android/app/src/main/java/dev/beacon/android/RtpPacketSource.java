package dev.beacon.android;

public interface RtpPacketSource {
    RtpPacket nextPacket();

    void close();

    static RtpPacketSource endOfStreamOnly() {
        return new RtpPacketSource() {
            @Override
            public RtpPacket nextPacket() {
                return null;
            }

            @Override
            public void close() {
            }
        };
    }
}
