package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class RepeatingAnnexBVideoSampleProviderTest {
    @Test
    public void repeatsCompleteAccessUnitSequenceWithDeterministicFrameTimes() {
        byte[] vector = new byte[] {
            0, 0, 0, 1, 0x67, 1,
            0, 0, 0, 1, 0x65, 2,
            0, 0, 0, 1, 0x41, 3
        };
        RepeatingAnnexBVideoSampleProvider provider =
            new RepeatingAnnexBVideoSampleProvider(vector, 60, 2);

        EncodedVideoSample first = provider.nextSample();
        EncodedVideoSample second = provider.nextSample();
        EncodedVideoSample third = provider.nextSample();
        EncodedVideoSample fourth = provider.nextSample();
        EncodedVideoSample end = provider.nextSample();

        assertFalse(first.endOfStream());
        assertEquals(0, first.presentationTimeUs());
        assertEquals(16_666, second.presentationTimeUs());
        assertEquals(33_333, third.presentationTimeUs());
        assertEquals(50_000, fourth.presentationTimeUs());
        assertTrue(end.endOfStream());
        assertEquals(4, provider.expectedFrameCount());
        assertEquals(12, provider.maxSampleBytes());
    }
}
