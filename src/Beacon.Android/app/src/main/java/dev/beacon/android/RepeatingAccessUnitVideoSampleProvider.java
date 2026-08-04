package dev.beacon.android;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

final class RepeatingAccessUnitVideoSampleProvider implements EncodedVideoSampleProvider {
    private static final byte[] Magic =
        "BEACONAU1\n".getBytes(StandardCharsets.US_ASCII);
    private final List<byte[]> accessUnits;
    private final int fps;
    private final int expectedFrameCount;
    private final int maxSampleBytes;
    private int nextFrame;

    RepeatingAccessUnitVideoSampleProvider(
        byte[] vector,
        int fps,
        int repetitionCount) {
        if (fps <= 0 || repetitionCount <= 0) {
            throw new IllegalArgumentException(
                "Benchmark frame rate and repetition count must be positive.");
        }
        accessUnits = parse(vector);
        this.fps = fps;
        expectedFrameCount = Math.multiplyExact(accessUnits.size(), repetitionCount);
        int largest = 0;
        for (byte[] accessUnit : accessUnits) {
            largest = Math.max(largest, accessUnit.length);
        }
        maxSampleBytes = largest;
    }

    @Override
    public synchronized EncodedVideoSample nextSample() {
        if (nextFrame >= expectedFrameCount) return EncodedVideoSample.eos();
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

    private static List<byte[]> parse(byte[] vector) {
        if (vector == null || vector.length < Magic.length + Integer.BYTES) {
            throw invalidVector();
        }
        ByteBuffer input = ByteBuffer.wrap(vector).order(ByteOrder.LITTLE_ENDIAN);
        byte[] magic = new byte[Magic.length];
        input.get(magic);
        if (!Arrays.equals(Magic, magic)) throw invalidVector();
        int count = input.getInt();
        if (count <= 0) throw invalidVector();
        List<byte[]> result = new ArrayList<>(count);
        for (int index = 0; index < count; index++) {
            if (input.remaining() < Integer.BYTES) throw invalidVector();
            int length = input.getInt();
            if (length <= 0 || length > input.remaining()) throw invalidVector();
            byte[] accessUnit = new byte[length];
            input.get(accessUnit);
            result.add(accessUnit);
        }
        if (input.hasRemaining()) throw invalidVector();
        return result;
    }

    private static IllegalArgumentException invalidVector() {
        return new IllegalArgumentException(
            "Benchmark vector is not a complete-access-unit container.");
    }
}
