package dev.beacon.android;

import java.util.Arrays;

public final class EncodedVideoSample {
    private static final EncodedVideoSample EndOfStream = new EncodedVideoSample(new byte[0], 0, true);

    private final byte[] data;
    private final long presentationTimeUs;
    private final boolean endOfStream;

    private EncodedVideoSample(byte[] data, long presentationTimeUs, boolean endOfStream) {
        this.data = data == null ? new byte[0] : Arrays.copyOf(data, data.length);
        this.presentationTimeUs = presentationTimeUs;
        this.endOfStream = endOfStream;
    }

    public static EncodedVideoSample data(byte[] data, long presentationTimeUs) {
        if (data == null || data.length == 0) {
            throw new IllegalArgumentException("Encoded video sample data is required.");
        }

        return new EncodedVideoSample(data, presentationTimeUs, false);
    }

    public static EncodedVideoSample eos() {
        return EndOfStream;
    }

    public byte[] data() {
        return Arrays.copyOf(data, data.length);
    }

    public long presentationTimeUs() {
        return presentationTimeUs;
    }

    public boolean endOfStream() {
        return endOfStream;
    }
}
