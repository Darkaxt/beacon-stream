package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;
import java.util.ArrayDeque;
import java.util.Queue;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;

public final class RtpDatagramPacketSourceTest {
    @Test
    public void receivesDatagramAsRtpPacket() {
        RecordingRtpDatagramSocket socket = new RecordingRtpDatagramSocket(
            packet(0x1234, 90000L, 0x01020304L, new byte[] {0, 0, 1, 0x65}));
        RtpDatagramPacketSource source = new RtpDatagramPacketSource(socket);

        RtpPacket packet = source.nextPacket();

        assertEquals(0x1234, packet.sequenceNumber());
        assertEquals(90000L, packet.timestamp());
        assertEquals(0x01020304L, packet.ssrc());
        assertArrayEquals(new byte[] {0, 0, 1, 0x65}, packet.payload());
    }

    @Test
    public void receiveFailureBecomesSourceDiagnostic() {
        RtpDatagramPacketSource source = new RtpDatagramPacketSource(new ThrowingRtpDatagramSocket());

        try {
            source.nextPacket();
        } catch (IllegalStateException ex) {
            assertEquals("RTP datagram receive failed: socket closed", ex.getMessage());
            return;
        }

        throw new AssertionError("Expected receive failure diagnostic.");
    }

    @Test
    public void closeDelegatesToSocketOnce() {
        RecordingRtpDatagramSocket socket = new RecordingRtpDatagramSocket();
        RtpDatagramPacketSource source = new RtpDatagramPacketSource(socket);

        source.close();
        source.close();

        assertEquals(1, socket.closeCount);
    }

    private static byte[] packet(int sequenceNumber, long timestamp, long ssrc, byte[] payload) {
        byte[] bytes = new byte[12 + payload.length];
        bytes[0] = (byte) 0x80;
        bytes[1] = 0x60;
        bytes[2] = (byte) ((sequenceNumber >>> 8) & 0xFF);
        bytes[3] = (byte) (sequenceNumber & 0xFF);
        bytes[4] = (byte) ((timestamp >>> 24) & 0xFF);
        bytes[5] = (byte) ((timestamp >>> 16) & 0xFF);
        bytes[6] = (byte) ((timestamp >>> 8) & 0xFF);
        bytes[7] = (byte) (timestamp & 0xFF);
        bytes[8] = (byte) ((ssrc >>> 24) & 0xFF);
        bytes[9] = (byte) ((ssrc >>> 16) & 0xFF);
        bytes[10] = (byte) ((ssrc >>> 8) & 0xFF);
        bytes[11] = (byte) (ssrc & 0xFF);
        System.arraycopy(payload, 0, bytes, 12, payload.length);
        return bytes;
    }

    private static final class RecordingRtpDatagramSocket implements RtpDatagramSocket {
        private final Queue<byte[]> datagrams = new ArrayDeque<>();
        private int closeCount;

        private RecordingRtpDatagramSocket(byte[]... datagrams) {
            for (byte[] datagram : datagrams) {
                this.datagrams.add(datagram);
            }
        }

        @Override
        public int receive(byte[] buffer) {
            byte[] datagram = datagrams.remove();
            System.arraycopy(datagram, 0, buffer, 0, datagram.length);
            return datagram.length;
        }

        @Override
        public int localPort() {
            return 50002;
        }

        @Override
        public void close() {
            closeCount++;
        }
    }

    private static final class ThrowingRtpDatagramSocket implements RtpDatagramSocket {
        @Override
        public int receive(byte[] buffer) throws IOException {
            throw new IOException("socket closed");
        }

        @Override
        public int localPort() {
            return 50002;
        }

        @Override
        public void close() {
        }
    }
}
