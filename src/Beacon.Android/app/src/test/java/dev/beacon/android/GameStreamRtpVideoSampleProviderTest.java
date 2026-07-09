package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayDeque;
import java.util.Queue;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

public final class GameStreamRtpVideoSampleProviderTest {
    @Test
    public void mapsRtpPayloadsToEncodedSamplesUsingVideoClock() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource(
            packet(1, 90000L, new byte[] {0, 0, 1, 0x65}),
            packet(2, 91500L, new byte[] {0, 0, 1, 0x41}));
        GameStreamRtpVideoSampleProvider provider = new GameStreamRtpVideoSampleProvider(source);

        EncodedVideoSample first = provider.nextSample();
        EncodedVideoSample second = provider.nextSample();
        EncodedVideoSample third = provider.nextSample();

        assertArrayEquals(new byte[] {0, 0, 1, 0x65}, first.data());
        assertEquals(0L, first.presentationTimeUs());
        assertArrayEquals(new byte[] {0, 0, 1, 0x41}, second.data());
        assertEquals(16666L, second.presentationTimeUs());
        assertTrue(third.endOfStream());
    }

    @Test
    public void ignoresEmptyRtpPayloads() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource(
            packet(1, 90000L, new byte[0]),
            packet(2, 90000L, new byte[] {0, 0, 1, 0x65}));
        GameStreamRtpVideoSampleProvider provider = new GameStreamRtpVideoSampleProvider(source);

        EncodedVideoSample sample = provider.nextSample();

        assertArrayEquals(new byte[] {0, 0, 1, 0x65}, sample.data());
        assertEquals(0L, sample.presentationTimeUs());
    }

    @Test
    public void mapsWrappedRtpTimestampToForwardPresentationTime() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource(
            packet(1, 0xFFFFFFF0L, new byte[] {0, 0, 1, 0x65}),
            packet(2, 0x00000020L, new byte[] {0, 0, 1, 0x41}));
        GameStreamRtpVideoSampleProvider provider = new GameStreamRtpVideoSampleProvider(source);

        EncodedVideoSample first = provider.nextSample();
        EncodedVideoSample second = provider.nextSample();

        assertEquals(0L, first.presentationTimeUs());
        assertEquals(533L, second.presentationTimeUs());
    }

    @Test
    public void sourceExceptionPropagatesAsProviderException() {
        GameStreamRtpVideoSampleProvider provider = new GameStreamRtpVideoSampleProvider(new ThrowingRtpPacketSource());

        try {
            provider.nextSample();
        } catch (IllegalStateException ex) {
            assertEquals("udp read failed", ex.getMessage());
            return;
        }

        throw new AssertionError("Expected packet source exception.");
    }

    private static RtpPacket packet(int sequenceNumber, long timestamp, byte[] payload) {
        byte[] bytes = new byte[12 + payload.length];
        bytes[0] = (byte) 0x80;
        bytes[1] = 0x60;
        bytes[2] = (byte) ((sequenceNumber >>> 8) & 0xFF);
        bytes[3] = (byte) (sequenceNumber & 0xFF);
        bytes[4] = (byte) ((timestamp >>> 24) & 0xFF);
        bytes[5] = (byte) ((timestamp >>> 16) & 0xFF);
        bytes[6] = (byte) ((timestamp >>> 8) & 0xFF);
        bytes[7] = (byte) (timestamp & 0xFF);
        bytes[11] = 0x01;
        System.arraycopy(payload, 0, bytes, 12, payload.length);
        return RtpPacket.parse(bytes);
    }

    private static final class RecordingRtpPacketSource implements RtpPacketSource {
        private final Queue<RtpPacket> packets = new ArrayDeque<>();

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
        }
    }

    private static final class ThrowingRtpPacketSource implements RtpPacketSource {
        @Override
        public RtpPacket nextPacket() {
            throw new IllegalStateException("udp read failed");
        }

        @Override
        public void close() {
        }
    }
}
