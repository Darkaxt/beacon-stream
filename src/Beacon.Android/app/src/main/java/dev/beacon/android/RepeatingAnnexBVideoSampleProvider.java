package dev.beacon.android;

import java.util.ArrayList;
import java.util.List;

final class RepeatingAnnexBVideoSampleProvider implements EncodedVideoSampleProvider {
    private final List<byte[]> accessUnits;
    private final int fps;
    private final int expectedFrameCount;
    private final int maxSampleBytes;
    private int nextFrame;

    RepeatingAnnexBVideoSampleProvider(byte[] vector, int fps, int repetitionCount) {
        if (fps <= 0 || repetitionCount <= 0) {
            throw new IllegalArgumentException("Benchmark frame rate and repetition count must be positive.");
        }
        this.accessUnits = copy(AnnexBAccessUnitSplitter.split(vector));
        if (accessUnits.isEmpty()) {
            throw new IllegalArgumentException("Benchmark vector contains no access units.");
        }
        this.fps = fps;
        this.expectedFrameCount = Math.multiplyExact(accessUnits.size(), repetitionCount);
        int largest = 0;
        for (byte[] accessUnit : accessUnits) {
            largest = Math.max(largest, accessUnit.length);
        }
        this.maxSampleBytes = largest;
    }

    @Override
    public synchronized EncodedVideoSample nextSample() {
        if (nextFrame >= expectedFrameCount) {
            return EncodedVideoSample.eos();
        }
        int unitIndex = nextFrame % accessUnits.size();
        long presentationTimeUs = nextFrame * 1_000_000L / fps;
        nextFrame++;
        return EncodedVideoSample.data(accessUnits.get(unitIndex), presentationTimeUs);
    }

    int expectedFrameCount() {
        return expectedFrameCount;
    }

    @Override
    public int maxSampleBytes() {
        return maxSampleBytes;
    }

    private static List<byte[]> copy(List<byte[]> values) {
        List<byte[]> copies = new ArrayList<>(values.size());
        for (byte[] value : values) {
            copies.add(value.clone());
        }
        return copies;
    }
}
