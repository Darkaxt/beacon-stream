package dev.beacon.android;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

final class BeaconAnnexBSampleEnvelopeParser {
    private static final byte[] Magic = "BEACONANNEXB1\n".getBytes(StandardCharsets.US_ASCII);
    private static final String BadMagic = "Encoded video sample stream is not a Beacon Annex B sample envelope.";

    private BeaconAnnexBSampleEnvelopeParser() {
    }

    static List<EncodedVideoSample> parse(byte[] bytes) {
        if (bytes == null || bytes.length < Magic.length || !hasMagic(bytes)) {
            throw new IllegalArgumentException(BadMagic);
        }

        ByteBuffer buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
        buffer.position(Magic.length);

        List<EncodedVideoSample> samples = new ArrayList<>();
        while (buffer.hasRemaining()) {
            if (buffer.remaining() < 12) {
                throw new IllegalArgumentException("Encoded video sample envelope has a truncated record header.");
            }

            long presentationTimeUs = buffer.getLong();
            int sampleLength = buffer.getInt();
            if (sampleLength <= 0) {
                throw new IllegalArgumentException("Encoded video sample envelope has an invalid sample length.");
            }
            if (buffer.remaining() < sampleLength) {
                throw new IllegalArgumentException("Encoded video sample envelope has a truncated sample.");
            }

            byte[] sample = new byte[sampleLength];
            buffer.get(sample);
            samples.add(EncodedVideoSample.data(sample, presentationTimeUs));
        }

        return samples;
    }

    private static boolean hasMagic(byte[] bytes) {
        return Arrays.equals(Magic, Arrays.copyOf(bytes, Magic.length));
    }
}
