package dev.beacon.android;

import org.junit.Test;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.List;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertThrows;

public final class BeaconAnnexBSampleEnvelopeParserTest {
    @Test
    public void parsesFramedAnnexBSamples() {
        byte[] first = new byte[] { 0, 0, 0, 1, 0x67, 0x01 };
        byte[] second = new byte[] { 0, 0, 1, 0x65, 0x02 };
        byte[] envelope = envelope(
            record(12_345L, first),
            record(67_890L, second));

        List<EncodedVideoSample> samples = BeaconAnnexBSampleEnvelopeParser.parse(envelope);

        assertEquals(2, samples.size());
        assertEquals(12_345L, samples.get(0).presentationTimeUs());
        assertArrayEquals(first, samples.get(0).data());
        assertEquals(67_890L, samples.get(1).presentationTimeUs());
        assertArrayEquals(second, samples.get(1).data());
    }

    @Test
    public void rejectsCorruptMagic() {
        IllegalArgumentException exception = assertThrows(
            IllegalArgumentException.class,
            () -> BeaconAnnexBSampleEnvelopeParser.parse("nope".getBytes(StandardCharsets.US_ASCII)));

        assertEquals(
            "Encoded video sample stream is not a Beacon Annex B sample envelope.",
            exception.getMessage());
    }

    private static byte[] envelope(byte[]... records) {
        return concat("BEACONANNEXB1\n".getBytes(StandardCharsets.US_ASCII), concat(records));
    }

    private static byte[] record(long presentationTimeUs, byte[] sample) {
        ByteBuffer buffer = ByteBuffer.allocate(12 + sample.length).order(ByteOrder.LITTLE_ENDIAN);
        buffer.putLong(presentationTimeUs);
        buffer.putInt(sample.length);
        buffer.put(sample);
        return buffer.array();
    }

    private static byte[] concat(byte[]... chunks) {
        int total = 0;
        for (byte[] chunk : chunks) {
            total += chunk.length;
        }

        byte[] result = new byte[total];
        int offset = 0;
        for (byte[] chunk : chunks) {
            System.arraycopy(chunk, 0, result, offset, chunk.length);
            offset += chunk.length;
        }

        return result;
    }
}
