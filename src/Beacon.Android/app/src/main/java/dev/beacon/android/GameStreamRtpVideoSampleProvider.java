package dev.beacon.android;

public final class GameStreamRtpVideoSampleProvider implements EncodedVideoSampleProvider {
    private static final long VideoClockHz = 90_000L;

    private final RtpPacketSource source;
    private boolean baseTimestampSet;
    private long baseTimestamp;

    public GameStreamRtpVideoSampleProvider(RtpPacketSource source) {
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
                return EncodedVideoSample.eos();
            }

            byte[] payload = packet.payload();
            if (payload.length == 0) {
                continue;
            }

            if (!baseTimestampSet) {
                baseTimestamp = packet.timestamp();
                baseTimestampSet = true;
            }

            return EncodedVideoSample.data(payload, presentationTimeUs(packet.timestamp()));
        }
    }

    private long presentationTimeUs(long timestamp) {
        return ((timestamp - baseTimestamp) * 1_000_000L) / VideoClockHz;
    }
}
