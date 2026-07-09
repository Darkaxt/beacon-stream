package dev.beacon.android;

import java.io.IOException;
import java.util.Arrays;

public final class RtpDatagramPacketSource implements RtpPacketSource {
    private static final int MaxDatagramBytes = 65_535;

    private final RtpDatagramSocket socket;
    private final byte[] buffer = new byte[MaxDatagramBytes];
    private boolean closed;

    public RtpDatagramPacketSource(RtpDatagramSocket socket) {
        if (socket == null) {
            throw new IllegalArgumentException("RTP datagram socket is required.");
        }

        this.socket = socket;
    }

    @Override
    public RtpPacket nextPacket() {
        try {
            int length = socket.receive(buffer);
            return RtpPacket.parse(Arrays.copyOf(buffer, length));
        } catch (IOException ex) {
            throw new IllegalStateException("RTP datagram receive failed: " + safeMessage(ex), ex);
        }
    }

    @Override
    public void close() {
        if (closed) {
            return;
        }

        closed = true;
        socket.close();
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null || message.isEmpty() ? throwable.getClass().getSimpleName() : message;
    }
}
