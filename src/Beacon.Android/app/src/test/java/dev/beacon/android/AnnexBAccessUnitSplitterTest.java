package dev.beacon.android;

import org.junit.Test;

import java.util.List;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertThrows;

public final class AnnexBAccessUnitSplitterTest {
    @Test
    public void groupsParameterSetsWithFollowingSlice() {
        byte[] bytes = concat(
            start(), new byte[] { 0x67, 0x01 },
            start(), new byte[] { 0x68, 0x02 },
            start(), new byte[] { 0x65, 0x03 },
            start(), new byte[] { 0x65, 0x04 });

        List<byte[]> samples = AnnexBAccessUnitSplitter.split(bytes);

        assertEquals(2, samples.size());
        assertArrayEquals(
            concat(start(), new byte[] { 0x67, 0x01 }, start(), new byte[] { 0x68, 0x02 }, start(), new byte[] { 0x65, 0x03 }),
            samples.get(0));
        assertArrayEquals(concat(start(), new byte[] { 0x65, 0x04 }), samples.get(1));
    }

    @Test
    public void rejectsBytesWithoutStartCode() {
        IllegalArgumentException exception = assertThrows(
            IllegalArgumentException.class,
            () -> AnnexBAccessUnitSplitter.split(new byte[] { 1, 2, 3 }));

        assertEquals("Encoded video bytes are not Annex B start-code delimited.", exception.getMessage());
    }

    private static byte[] start() {
        return new byte[] { 0, 0, 0, 1 };
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
