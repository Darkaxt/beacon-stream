package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayDeque;
import java.util.Queue;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

public final class H264RtpSampleProviderTest {
    @Test
    public void mapsSingleNalPacketToAnnexBSample() {
        H264RtpSampleProvider provider = new H264RtpSampleProvider(new RecordingRtpPacketSource(
            packet(1, 90000L, new byte[] {0x65, 0x11, 0x22})));

        EncodedVideoSample sample = provider.nextSample();
        EncodedVideoSample eos = provider.nextSample();

        assertArrayEquals(new byte[] {0, 0, 0, 1, 0x65, 0x11, 0x22}, sample.data());
        assertEquals(0L, sample.presentationTimeUs());
        assertTrue(eos.endOfStream());
    }

    @Test
    public void mapsStapAPacketToOneAnnexBSampleWithMultipleNals() {
        H264RtpSampleProvider provider = new H264RtpSampleProvider(new RecordingRtpPacketSource(
            packet(1, 90000L, new byte[] {
                0x18,
                0x00, 0x02, 0x67, 0x01,
                0x00, 0x02, 0x68, 0x02
            })));

        EncodedVideoSample sample = provider.nextSample();

        assertArrayEquals(new byte[] {
            0, 0, 0, 1, 0x67, 0x01,
            0, 0, 0, 1, 0x68, 0x02
        }, sample.data());
        assertEquals(0L, sample.presentationTimeUs());
    }

    @Test
    public void mapsFuAFragmentsToOneAnnexBSample() {
        H264RtpSampleProvider provider = new H264RtpSampleProvider(new RecordingRtpPacketSource(
            packet(1, 90000L, new byte[] {
                0x7C,
                (byte) 0x85,
                0x11,
                0x22
            }),
            packet(2, 90000L, new byte[] {
                0x7C,
                0x45,
                0x33
            })));

        EncodedVideoSample sample = provider.nextSample();

        assertArrayEquals(new byte[] {0, 0, 0, 1, 0x65, 0x11, 0x22, 0x33}, sample.data());
        assertEquals(0L, sample.presentationTimeUs());
    }

    @Test
    public void mapsWrappedRtpTimestampToForwardPresentationTime() {
        H264RtpSampleProvider provider = new H264RtpSampleProvider(new RecordingRtpPacketSource(
            packet(1, 0xFFFFFFF0L, new byte[] {0x65, 0x11}),
            packet(2, 0x00000020L, new byte[] {0x41, 0x22})));

        EncodedVideoSample first = provider.nextSample();
        EncodedVideoSample second = provider.nextSample();

        assertEquals(0L, first.presentationTimeUs());
        assertEquals(533L, second.presentationTimeUs());
    }

    @Test
    public void rejectsUnsupportedPacketizationType() {
        assertPayloadFailure(
            "H.264 RTP payload failed: unsupported H.264 packetization type 25.",
            packet(1, 90000L, new byte[] {0x19, 0x01}));
    }

    @Test
    public void rejectsTruncatedStapASize() {
        assertPayloadFailure(
            "H.264 RTP payload failed: truncated STAP-A NAL size.",
            packet(1, 90000L, new byte[] {0x18, 0x00}));
    }

    @Test
    public void rejectsTruncatedStapANal() {
        assertPayloadFailure(
            "H.264 RTP payload failed: truncated STAP-A NAL payload.",
            packet(1, 90000L, new byte[] {0x18, 0x00, 0x03, 0x67, 0x01}));
    }

    @Test
    public void rejectsFuAEndWithoutStart() {
        assertPayloadFailure(
            "H.264 RTP payload failed: FU-A continuation without start.",
            packet(1, 90000L, new byte[] {0x7C, 0x45, 0x33}));
    }

    @Test
    public void rejectsFuATimestampChangeDuringFragment() {
        H264RtpSampleProvider provider = new H264RtpSampleProvider(new RecordingRtpPacketSource(
            packet(1, 90000L, new byte[] {0x7C, (byte) 0x85, 0x11}),
            packet(2, 90001L, new byte[] {0x7C, 0x45, 0x22})));

        try {
            provider.nextSample();
        } catch (IllegalStateException ex) {
            assertEquals("H.264 RTP payload failed: FU-A timestamp changed before end fragment.", ex.getMessage());
            return;
        }

        throw new AssertionError("Expected H.264 payload failure.");
    }

    private static void assertPayloadFailure(String message, RtpPacket packet) {
        H264RtpSampleProvider provider = new H264RtpSampleProvider(new RecordingRtpPacketSource(packet));

        try {
            provider.nextSample();
        } catch (IllegalStateException ex) {
            assertEquals(message, ex.getMessage());
            return;
        }

        throw new AssertionError("Expected H.264 payload failure.");
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
}
