package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicLong;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class PacedEncodedVideoSampleProviderTest {
    @Test
    public void releasesSamplesAtPresentationCadenceWithoutDelayingEndOfStream() {
        AtomicLong nowNs = new AtomicLong(1_000_000L);
        List<Long> waits = new ArrayList<>();
        EncodedVideoSampleProvider source = new EncodedVideoSampleProvider() {
            private int index;

            @Override
            public EncodedVideoSample nextSample() {
                return switch (index++) {
                    case 0 -> EncodedVideoSample.data(new byte[] { 1 }, 0);
                    case 1 -> EncodedVideoSample.data(new byte[] { 2 }, 16_666);
                    case 2 -> EncodedVideoSample.data(new byte[] { 3 }, 33_333);
                    default -> EncodedVideoSample.eos();
                };
            }

            @Override
            public int maxSampleBytes() {
                return 3;
            }
        };
        PacedEncodedVideoSampleProvider provider = new PacedEncodedVideoSampleProvider(
            source,
            nowNs::get,
            durationNs -> {
                waits.add(durationNs);
                nowNs.addAndGet(durationNs);
            });

        assertFalse(provider.nextSample().endOfStream());
        assertFalse(provider.nextSample().endOfStream());
        assertFalse(provider.nextSample().endOfStream());
        assertTrue(provider.nextSample().endOfStream());

        assertEquals(List.of(16_666_000L, 16_667_000L), waits);
        assertEquals(3, provider.maxSampleBytes());
    }
}
