package dev.beacon.android;

import java.util.Arrays;

public final class EncodedVideoOutputFormat {
    private final String mimeType;
    private final int profile;
    private final int colorStandard;
    private final int colorTransfer;
    private final int colorRange;
    private final byte[] hdrStaticInfo;

    public EncodedVideoOutputFormat(
        String mimeType,
        int profile,
        int colorStandard,
        int colorTransfer,
        int colorRange,
        byte[] hdrStaticInfo) {
        this.mimeType = mimeType == null ? "" : mimeType;
        this.profile = profile;
        this.colorStandard = colorStandard;
        this.colorTransfer = colorTransfer;
        this.colorRange = colorRange;
        this.hdrStaticInfo = hdrStaticInfo == null
            ? new byte[0]
            : Arrays.copyOf(hdrStaticInfo, hdrStaticInfo.length);
    }

    public String mimeType() { return mimeType; }
    public int profile() { return profile; }
    public int colorStandard() { return colorStandard; }
    public int colorTransfer() { return colorTransfer; }
    public int colorRange() { return colorRange; }
    public byte[] hdrStaticInfo() { return Arrays.copyOf(hdrStaticInfo, hdrStaticInfo.length); }
}
