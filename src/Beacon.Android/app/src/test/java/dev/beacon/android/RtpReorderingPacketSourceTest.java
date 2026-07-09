package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayDeque;
import java.util.Queue;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;

public final class RtpReorderingPacketSourceTest {
    @Test
    public void rejectsMissingInnerSource() {
        try {
            new RtpReorderingPacketSource(null, 4);
        } catch (IllegalArgumentException ex) {
            assertEquals("RTP packet source is required.", ex.getMessage());
            return;
        }

        throw new AssertionError("Expected missing source failure.");
    }

    @Test
    public void rejectsEmptyReorderWindow() {
        try {
            new RtpReorderingPacketSource(new RecordingRtpPacketSource(), 0);
        } catch (IllegalArgumentException ex) {
            assertEquals("RTP reorder window must be at least one packet.", ex.getMessage());
            return;
        }

        throw new AssertionError("Expected empty reorder window failure.");
    }

    @Test
    public void passesThroughPacketsThatArriveInOrder() {
        RtpReorderingPacketSource source = new RtpReorderingPacketSource(
            new RecordingRtpPacketSource(packet(10), packet(11), packet(12)),
            4);

        assertEquals(10, source.nextPacket().sequenceNumber());
        assertEquals(11, source.nextPacket().sequenceNumber());
        assertEquals(12, source.nextPacket().sequenceNumber());
        assertNull(source.nextPacket());
    }

    @Test
    public void reordersFuturePacketWhenMissingPacketArrives() {
        RtpReorderingPacketSource source = new RtpReorderingPacketSource(
            new RecordingRtpPacketSource(packet(10), packet(12), packet(11), packet(13)),
            4);

        assertEquals(10, source.nextPacket().sequenceNumber());
        assertEquals(11, source.nextPacket().sequenceNumber());
        assertEquals(12, source.nextPacket().sequenceNumber());
        assertEquals(13, source.nextPacket().sequenceNumber());
        assertNull(source.nextPacket());
    }

    @Test
    public void dropsDuplicateAndLatePackets() {
        RtpReorderingPacketSource source = new RtpReorderingPacketSource(
            new RecordingRtpPacketSource(packet(10), packet(11), packet(11), packet(10), packet(12)),
            4);

        assertEquals(10, source.nextPacket().sequenceNumber());
        assertEquals(11, source.nextPacket().sequenceNumber());
        assertEquals(12, source.nextPacket().sequenceNumber());
        assertNull(source.nextPacket());
    }

    @Test
    public void reordersAcrossSequenceWraparound() {
        RtpReorderingPacketSource source = new RtpReorderingPacketSource(
            new RecordingRtpPacketSource(packet(65534), packet(0), packet(65535), packet(1)),
            4);

        assertEquals(65534, source.nextPacket().sequenceNumber());
        assertEquals(65535, source.nextPacket().sequenceNumber());
        assertEquals(0, source.nextPacket().sequenceNumber());
        assertEquals(1, source.nextPacket().sequenceNumber());
        assertNull(source.nextPacket());
    }

    @Test
    public void advancesPastMissingPacketWhenReorderWindowIsFull() {
        RtpReorderingPacketSource source = new RtpReorderingPacketSource(
            new RecordingRtpPacketSource(packet(10), packet(12), packet(13), packet(14)),
            2);

        assertEquals(10, source.nextPacket().sequenceNumber());
        assertEquals(12, source.nextPacket().sequenceNumber());
        assertEquals(13, source.nextPacket().sequenceNumber());
        assertEquals(14, source.nextPacket().sequenceNumber());
        assertNull(source.nextPacket());
    }

    @Test
    public void closeDelegatesToInnerSourceOnce() {
        RecordingRtpPacketSource inner = new RecordingRtpPacketSource();
        RtpReorderingPacketSource source = new RtpReorderingPacketSource(inner, 4);

        source.close();
        source.close();

        assertEquals(1, inner.closeCount);
    }

    private static RtpPacket packet(int sequenceNumber) {
        byte[] bytes = new byte[13];
        bytes[0] = (byte) 0x80;
        bytes[1] = 0x60;
        bytes[2] = (byte) ((sequenceNumber >>> 8) & 0xFF);
        bytes[3] = (byte) (sequenceNumber & 0xFF);
        bytes[4] = 0x00;
        bytes[5] = 0x01;
        bytes[6] = (byte) ((sequenceNumber >>> 8) & 0xFF);
        bytes[7] = (byte) (sequenceNumber & 0xFF);
        bytes[11] = 0x01;
        bytes[12] = (byte) sequenceNumber;
        return RtpPacket.parse(bytes);
    }

    private static final class RecordingRtpPacketSource implements RtpPacketSource {
        private final Queue<RtpPacket> packets = new ArrayDeque<>();
        private int closeCount;

        private RecordingRtpPacketSource(RtpPacket... packets) {
            for (RtpPacket packet : packets) {
                this.packets.add(packet);
            }
        }

        @Override
        public RtpPacket nextPacket() {
            return packets.poll();
        }

        @Override
        public void close() {
            closeCount++;
        }
    }
}
