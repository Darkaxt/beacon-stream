package dev.beacon.android;

import java.io.ByteArrayOutputStream;

public final class H264RtpSampleProvider implements EncodedVideoSampleProvider {
    private static final byte[] AnnexBStartCode = new byte[] {0, 0, 0, 1};
    private static final long VideoClockHz = 90_000L;
    private static final long TimestampMask = 0xFFFFFFFFL;

    private final RtpPacketSource source;
    private boolean baseTimestampSet;
    private long baseTimestamp;
    private ByteArrayOutputStream activeFragment;
    private long activeFragmentTimestamp;

    public H264RtpSampleProvider(RtpPacketSource source) {
        if (source == null) {
            throw new IllegalArgumentException("RTP packet source is required.");
        }

        this.source = source;
    }

    @Override
    public EncodedVideoSample nextSample() {
        while (true) {
            RtpPacket packet = source.nextPacket();
            if (packet == null) {
                if (activeFragment != null) {
                    throw payloadFailure("FU-A stream ended before end fragment.");
                }

                return EncodedVideoSample.eos();
            }

            byte[] payload = packet.payload();
            if (payload.length == 0) {
                continue;
            }

            EncodedVideoSample sample = sampleFromPayload(packet, payload);
            if (sample != null) {
                return sample;
            }
        }
    }

    private EncodedVideoSample sampleFromPayload(RtpPacket packet, byte[] payload) {
        int nalType = payload[0] & 0x1F;
        if (nalType >= 1 && nalType <= 23) {
            return sample(packet.timestamp(), annexB(payload));
        }

        if (nalType == 24) {
            return sample(packet.timestamp(), stapA(payload));
        }

        if (nalType == 28) {
            return fuA(packet.timestamp(), payload);
        }

        throw payloadFailure("unsupported H.264 packetization type " + nalType + ".");
    }

    private EncodedVideoSample fuA(long timestamp, byte[] payload) {
        if (payload.length < 2) {
            throw payloadFailure("truncated FU-A header.");
        }

        int indicator = payload[0] & 0xFF;
        int header = payload[1] & 0xFF;
        boolean start = (header & 0x80) != 0;
        boolean end = (header & 0x40) != 0;
        boolean reserved = (header & 0x20) != 0;
        int nalType = header & 0x1F;

        if (reserved) {
            throw payloadFailure("FU-A reserved bit is set.");
        }

        if (start && end) {
            throw payloadFailure("FU-A start and end bits are both set.");
        }

        if (start) {
            activeFragment = new ByteArrayOutputStream();
            activeFragmentTimestamp = timestamp;
            writeStartCode(activeFragment);
            activeFragment.write((indicator & 0xE0) | nalType);
            activeFragment.write(payload, 2, payload.length - 2);
            return null;
        }

        if (activeFragment == null) {
            throw payloadFailure("FU-A continuation without start.");
        }

        if (activeFragmentTimestamp != timestamp) {
            activeFragment = null;
            throw payloadFailure("FU-A timestamp changed before end fragment.");
        }

        activeFragment.write(payload, 2, payload.length - 2);
        if (!end) {
            return null;
        }

        byte[] sample = activeFragment.toByteArray();
        activeFragment = null;
        return sample(timestamp, sample);
    }

    private byte[] stapA(byte[] payload) {
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        int offset = 1;
        while (offset < payload.length) {
            if (offset + 2 > payload.length) {
                throw payloadFailure("truncated STAP-A NAL size.");
            }

            int nalLength = ((payload[offset] & 0xFF) << 8) | (payload[offset + 1] & 0xFF);
            offset += 2;
            if (nalLength <= 0) {
                throw payloadFailure("empty STAP-A NAL payload.");
            }

            if (offset + nalLength > payload.length) {
                throw payloadFailure("truncated STAP-A NAL payload.");
            }

            writeStartCode(output);
            output.write(payload, offset, nalLength);
            offset += nalLength;
        }

        return output.toByteArray();
    }

    private static byte[] annexB(byte[] payload) {
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        writeStartCode(output);
        output.write(payload, 0, payload.length);
        return output.toByteArray();
    }

    private EncodedVideoSample sample(long timestamp, byte[] data) {
        if (!baseTimestampSet) {
            baseTimestamp = timestamp;
            baseTimestampSet = true;
        }

        long elapsedTicks = (timestamp - baseTimestamp) & TimestampMask;
        return EncodedVideoSample.data(data, (elapsedTicks * 1_000_000L) / VideoClockHz);
    }

    private static void writeStartCode(ByteArrayOutputStream output) {
        output.write(AnnexBStartCode, 0, AnnexBStartCode.length);
    }

    private static IllegalStateException payloadFailure(String message) {
        return new IllegalStateException("H.264 RTP payload failed: " + message);
    }
}
