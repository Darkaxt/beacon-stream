package dev.beacon.android;

import org.junit.Test;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

public final class RepeatingAccessUnitVideoSampleProviderTest {
    @Test
    public void repeatsPrepackedAccessUnitsWithDeterministicFrameTimes() {
        byte[] firstUnit = new byte[] { 0, 0, 0, 1, 0x65, 1 };
        byte[] secondUnit = new byte[] { 0, 0, 1, 0x41, 2 };
        RepeatingAccessUnitVideoSampleProvider provider =
            new RepeatingAccessUnitVideoSampleProvider(
                vector(firstUnit, secondUnit), 60, 2);

        EncodedVideoSample first = provider.nextSample();
        EncodedVideoSample second = provider.nextSample();
        EncodedVideoSample third = provider.nextSample();
        EncodedVideoSample fourth = provider.nextSample();
        EncodedVideoSample end = provider.nextSample();

        assertFalse(first.endOfStream());
        assertArrayEquals(firstUnit, first.data());
        assertArrayEquals(secondUnit, second.data());
        assertArrayEquals(firstUnit, third.data());
        assertEquals(0, first.presentationTimeUs());
        assertEquals(16_666, second.presentationTimeUs());
        assertEquals(33_333, third.presentationTimeUs());
        assertEquals(50_000, fourth.presentationTimeUs());
        assertTrue(end.endOfStream());
        assertEquals(4, provider.expectedFrameCount());
        assertEquals(6, provider.maxSampleBytes());
    }

    @Test
    public void rejectsTruncatedOrTrailingContainerBytes() {
        byte[] valid = vector(new byte[] { 1, 2, 3 });
        byte[] truncated = java.util.Arrays.copyOf(valid, valid.length - 1);
        byte[] trailing = java.util.Arrays.copyOf(valid, valid.length + 1);

        assertThrows(
            IllegalArgumentException.class,
            () -> new RepeatingAccessUnitVideoSampleProvider(truncated, 60, 1));
        assertThrows(
            IllegalArgumentException.class,
            () -> new RepeatingAccessUnitVideoSampleProvider(trailing, 60, 1));
    }

    private static byte[] vector(byte[]... accessUnits) {
        int size = "BEACONAU1\n".length() + Integer.BYTES;
        for (byte[] accessUnit : accessUnits) {
            size += Integer.BYTES + accessUnit.length;
        }
        ByteBuffer output = ByteBuffer.allocate(size).order(ByteOrder.LITTLE_ENDIAN);
        output.put("BEACONAU1\n".getBytes(StandardCharsets.US_ASCII));
        output.putInt(accessUnits.length);
        for (byte[] accessUnit : accessUnits) {
            output.putInt(accessUnit.length);
            output.put(accessUnit);
        }
        return output.array();
    }
}
