package dev.beacon.android;

import java.nio.ByteBuffer;

public final class EncodedVideoSample {
    private static final EncodedVideoSample EndOfStream = new EncodedVideoSample(
        ByteBuffer.allocateDirect(0), 0, 0, false, false, true);

    private final ByteBuffer data;
    private final long presentationTimeUs;
    private final long sequence;
    private final boolean idr;
    private final boolean codecConfiguration;
    private final boolean endOfStream;

    private EncodedVideoSample(
        ByteBuffer data,
        long presentationTimeUs,
        long sequence,
        boolean idr,
        boolean codecConfiguration,
        boolean endOfStream) {
        if (data == null || !data.isDirect()) {
            throw new IllegalArgumentException("Encoded video sample requires a direct buffer.");
        }
        this.data = data.asReadOnlyBuffer().slice().asReadOnlyBuffer();
        this.presentationTimeUs = presentationTimeUs;
        this.sequence = sequence;
        this.idr = idr;
        this.codecConfiguration = codecConfiguration;
        this.endOfStream = endOfStream;
    }

    public static EncodedVideoSample data(byte[] data, long presentationTimeUs) {
        if (data == null || data.length == 0) {
            throw new IllegalArgumentException("Encoded video sample data is required.");
        }
        ByteBuffer direct = ByteBuffer.allocateDirect(data.length);
        direct.put(data);
        direct.flip();
        return data(direct, presentationTimeUs, 0, false, false);
    }

    static EncodedVideoSample data(
        ByteBuffer data,
        long presentationTimeUs,
        long sequence,
        boolean idr,
        boolean codecConfiguration) {
        if (data == null || !data.hasRemaining()) {
            throw new IllegalArgumentException("Encoded video sample data is required.");
        }

        return new EncodedVideoSample(
            data,
            presentationTimeUs,
            sequence,
            idr,
            codecConfiguration,
            false);
    }

    public static EncodedVideoSample eos() {
        return EndOfStream;
    }

    public byte[] data() {
        ByteBuffer source = data.asReadOnlyBuffer();
        byte[] copy = new byte[source.remaining()];
        source.get(copy);
        return copy;
    }

    ByteBuffer dataBuffer() {
        return data.asReadOnlyBuffer();
    }

    public long presentationTimeUs() {
        return presentationTimeUs;
    }

    public long sequence() {
        return sequence;
    }

    public boolean idr() {
        return idr;
    }

    public boolean codecConfiguration() {
        return codecConfiguration;
    }

    public boolean endOfStream() {
        return endOfStream;
    }
}
