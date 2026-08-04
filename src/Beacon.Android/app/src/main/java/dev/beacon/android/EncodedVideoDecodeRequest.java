package dev.beacon.android;

public final class EncodedVideoDecodeRequest {
    private final String codec;
    private final int width;
    private final int height;
    private final int fps;
    private final String profile;
    private final int bitDepth;
    private final String dynamicRange;
    private final String colorPrimaries;
    private final String transferFunction;
    private final String matrixCoefficients;
    private final String colorRange;
    private final byte[] hdrStaticInfo;
    private final boolean hdrStaticInfoInBitstream;
    private final EncodedVideoSampleProvider sampleProvider;

    public EncodedVideoDecodeRequest(
        String codec,
        int width,
        int height,
        int fps,
        EncodedVideoSampleProvider sampleProvider) {
        this(codec, width, height, fps,
            "h264High", 8, "sdr", "bt709", "bt709", "bt709", "limited",
            new byte[0], false, sampleProvider);
    }

    public EncodedVideoDecodeRequest(
        String codec,
        int width,
        int height,
        int fps,
        String profile,
        int bitDepth,
        String dynamicRange,
        String colorPrimaries,
        String transferFunction,
        String matrixCoefficients,
        String colorRange,
        byte[] hdrStaticInfo,
        boolean hdrStaticInfoInBitstream,
        EncodedVideoSampleProvider sampleProvider) {
        if (codec == null || codec.isBlank()) {
            throw new IllegalArgumentException("Encoded video codec is required.");
        }
        if (width <= 0 || height <= 0 || fps <= 0) {
            throw new IllegalArgumentException("Encoded video dimensions and FPS must be positive.");
        }
        if (sampleProvider == null) {
            throw new IllegalArgumentException("Encoded video sample provider is required.");
        }

        this.codec = codec.trim();
        this.width = width;
        this.height = height;
        this.fps = fps;
        this.profile = requireText(profile, "Encoded video profile");
        this.bitDepth = bitDepth;
        this.dynamicRange = requireText(dynamicRange, "Encoded video dynamic range");
        this.colorPrimaries = requireText(colorPrimaries, "Encoded video color primaries");
        this.transferFunction = requireText(transferFunction, "Encoded video transfer function");
        this.matrixCoefficients = requireText(matrixCoefficients, "Encoded video matrix coefficients");
        this.colorRange = requireText(colorRange, "Encoded video color range");
        this.hdrStaticInfo = hdrStaticInfo == null ? new byte[0] : hdrStaticInfo.clone();
        this.hdrStaticInfoInBitstream = hdrStaticInfoInBitstream;
        this.sampleProvider = sampleProvider;
        validateHdr10Tuple();
    }

    public String codec() { return codec; }

    public int width() { return width; }

    public int height() { return height; }

    public int fps() { return fps; }

    public String profile() { return profile; }

    public int bitDepth() { return bitDepth; }

    public String dynamicRange() { return dynamicRange; }

    public String colorPrimaries() { return colorPrimaries; }

    public String transferFunction() { return transferFunction; }

    public String matrixCoefficients() { return matrixCoefficients; }

    public String colorRange() { return colorRange; }

    public byte[] hdrStaticInfo() { return hdrStaticInfo.clone(); }

    public boolean hdrStaticInfoInBitstream() { return hdrStaticInfoInBitstream; }

    public boolean isHevcMain10Hdr10() {
        return "hevc".equalsIgnoreCase(codec) && "hdr10".equalsIgnoreCase(dynamicRange);
    }

    public EncodedVideoSampleProvider sampleProvider() { return sampleProvider; }

    private void validateHdr10Tuple() {
        if (!"hdr10".equalsIgnoreCase(dynamicRange)) return;
        if (!"hevc".equalsIgnoreCase(codec) ||
            !"hevcMain10".equalsIgnoreCase(profile) ||
            bitDepth != 10 ||
            !"bt2020".equalsIgnoreCase(colorPrimaries) ||
            !"pq".equalsIgnoreCase(transferFunction) ||
            !"bt2020NonConstantLuminance".equalsIgnoreCase(matrixCoefficients) ||
            !"limited".equalsIgnoreCase(colorRange) ||
            hdrStaticInfo.length != 25) {
            throw new IllegalArgumentException("Encoded HDR10 mode must be exact HEVC Main10 BT.2020 PQ limited-range with 25-byte static metadata.");
        }
    }

    private static String requireText(String value, String name) {
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException(name + " is required.");
        }
        return value.trim();
    }
}
