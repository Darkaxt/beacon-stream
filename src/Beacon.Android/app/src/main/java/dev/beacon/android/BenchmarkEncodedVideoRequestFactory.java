package dev.beacon.android;

final class BenchmarkEncodedVideoRequestFactory {
    private static final String HEVC_HDR10_VECTOR =
        "beacon-hevc-main10-hdr10-320x180-30-v1";
    private static final byte[] HDR_STATIC_INFO = new byte[] {
        0, 0x48, (byte) 0x8a, 0x08, 0x39, 0x34, 0x21, (byte) 0xaa,
        (byte) 0x9b, (byte) 0x96, 0x19, (byte) 0xfc, 0x08, 0x13, 0x3d,
        0x42, 0x40, (byte) 0xe8, 0x03, 0x32, 0x00, (byte) 0xe8, 0x03,
        (byte) 0x90, 0x01};

    private BenchmarkEncodedVideoRequestFactory() { }

    static EncodedVideoDecodeRequest create(
        BeaconBenchmarkHardwarePlan.DecoderRound round,
        EncodedVideoSampleProvider samples) {
        if (HEVC_HDR10_VECTOR.equals(round.vectorId()) &&
            "hevc".equalsIgnoreCase(round.codec()) &&
            ("main10".equalsIgnoreCase(round.profile()) ||
                "hevcMain10".equalsIgnoreCase(round.profile())) &&
            round.bitDepth() == 10) {
            return new EncodedVideoDecodeRequest(
                round.codec(), round.width(), round.height(), round.targetFps(),
                "hevcMain10", 10, "hdr10", "bt2020", "pq",
                "bt2020NonConstantLuminance", "limited", hdrStaticInfo(), true,
                samples);
        }
        return new EncodedVideoDecodeRequest(
            round.codec(), round.width(), round.height(), round.targetFps(), samples);
    }

    static byte[] hdrStaticInfo() { return HDR_STATIC_INFO.clone(); }
}
