package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;
import static org.junit.Assert.fail;

public final class RtpPacketTest {
    @Test
    public void parsesBasicRtpPacket() {
        byte[] bytes = new byte[] {
            (byte) 0x80,
            (byte) 0xE0,
            0x12,
            0x34,
            0x00,
            0x01,
            0x5F,
            (byte) 0x90,
            0x01,
            0x02,
            0x03,
            0x04,
            0x00,
            0x00,
            0x01,
            0x65
        };

        RtpPacket packet = RtpPacket.parse(bytes);
        byte[] payload = packet.payload();
        payload[0] = 0x55;

        assertTrue(packet.marker());
        assertEquals(96, packet.payloadType());
        assertEquals(0x1234, packet.sequenceNumber());
        assertEquals(90000L, packet.timestamp());
        assertEquals(0x01020304L, packet.ssrc());
        assertArrayEquals(new byte[] {0x00, 0x00, 0x01, 0x65}, packet.payload());
    }

    @Test
    public void rejectsPacketShorterThanHeader() {
        try {
            RtpPacket.parse(new byte[11]);
            fail("Expected parser to reject short RTP packet.");
        } catch (IllegalArgumentException ex) {
            assertEquals("RTP packet must be at least 12 bytes.", ex.getMessage());
        }
    }

    @Test
    public void rejectsUnsupportedRtpVersion() {
        try {
            RtpPacket.parse(new byte[] {
                (byte) 0x40,
                0x60,
                0x00,
                0x01,
                0x00,
                0x00,
                0x00,
                0x01,
                0x00,
                0x00,
                0x00,
                0x01
            });
            fail("Expected parser to reject non-v2 RTP packet.");
        } catch (IllegalArgumentException ex) {
            assertEquals("Unsupported RTP version: 1.", ex.getMessage());
        }
    }

    @Test
    public void skipsCsrcAndExtensionHeader() {
        byte[] bytes = new byte[] {
            (byte) 0x91,
            0x60,
            0x00,
            0x02,
            0x00,
            0x00,
            0x00,
            0x10,
            0x01,
            0x02,
            0x03,
            0x04,
            0x11,
            0x22,
            0x33,
            0x44,
            (byte) 0xBE,
            (byte) 0xDE,
            0x00,
            0x01,
            0x55,
            0x66,
            0x77,
            0x00,
            0x01,
            0x02,
            0x03
        };

        RtpPacket packet = RtpPacket.parse(bytes);

        assertFalse(packet.marker());
        assertEquals(96, packet.payloadType());
        assertEquals(2, packet.sequenceNumber());
        assertEquals(16L, packet.timestamp());
        assertEquals(0x01020304L, packet.ssrc());
        assertArrayEquals(new byte[] {0x01, 0x02, 0x03}, packet.payload());
    }
}
