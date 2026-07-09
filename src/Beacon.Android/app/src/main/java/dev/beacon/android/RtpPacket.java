package dev.beacon.android;

import java.util.Arrays;

public final class RtpPacket {
    private static final int FixedHeaderLength = 12;

    private final boolean marker;
    private final int payloadType;
    private final int sequenceNumber;
    private final long timestamp;
    private final long ssrc;
    private final byte[] payload;

    private RtpPacket(
        boolean marker,
        int payloadType,
        int sequenceNumber,
        long timestamp,
        long ssrc,
        byte[] payload) {
        this.marker = marker;
        this.payloadType = payloadType;
        this.sequenceNumber = sequenceNumber;
        this.timestamp = timestamp;
        this.ssrc = ssrc;
        this.payload = payload == null ? new byte[0] : Arrays.copyOf(payload, payload.length);
    }

    public static RtpPacket parse(byte[] bytes) {
        if (bytes == null || bytes.length < FixedHeaderLength) {
            throw new IllegalArgumentException("RTP packet must be at least 12 bytes.");
        }

        int version = (bytes[0] >>> 6) & 0x03;
        if (version != 2) {
            throw new IllegalArgumentException("Unsupported RTP version: " + version + ".");
        }

        boolean extension = ((bytes[0] >>> 4) & 0x01) == 1;
        int csrcCount = bytes[0] & 0x0F;
        int payloadOffset = FixedHeaderLength + (csrcCount * 4);
        if (payloadOffset > bytes.length) {
            throw new IllegalArgumentException("RTP packet CSRC list is truncated.");
        }

        if (extension) {
            if (payloadOffset + 4 > bytes.length) {
                throw new IllegalArgumentException("RTP packet extension header is truncated.");
            }

            int extensionLengthWords = unsignedShort(bytes, payloadOffset + 2);
            payloadOffset += 4;
            int extensionBytes = extensionLengthWords * 4;
            if (payloadOffset + extensionBytes > bytes.length) {
                throw new IllegalArgumentException("RTP packet extension payload is truncated.");
            }

            payloadOffset += extensionBytes;
        }

        return new RtpPacket(
            (bytes[1] & 0x80) != 0,
            bytes[1] & 0x7F,
            unsignedShort(bytes, 2),
            unsignedInt(bytes, 4),
            unsignedInt(bytes, 8),
            Arrays.copyOfRange(bytes, payloadOffset, bytes.length));
    }

    public boolean marker() {
        return marker;
    }

    public int payloadType() {
        return payloadType;
    }

    public int sequenceNumber() {
        return sequenceNumber;
    }

    public long timestamp() {
        return timestamp;
    }

    public long ssrc() {
        return ssrc;
    }

    public byte[] payload() {
        return Arrays.copyOf(payload, payload.length);
    }

    private static int unsignedShort(byte[] bytes, int offset) {
        return ((bytes[offset] & 0xFF) << 8) | (bytes[offset + 1] & 0xFF);
    }

    private static long unsignedInt(byte[] bytes, int offset) {
        return ((long) (bytes[offset] & 0xFF) << 24) |
            ((long) (bytes[offset + 1] & 0xFF) << 16) |
            ((long) (bytes[offset + 2] & 0xFF) << 8) |
            (long) (bytes[offset + 3] & 0xFF);
    }
}
