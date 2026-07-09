package dev.beacon.android;

import java.util.Locale;

public final class AndroidDeviceCapabilityProbe {
    private final AndroidCodecCatalog codecCatalog;

    public AndroidDeviceCapabilityProbe(AndroidCodecCatalog codecCatalog) {
        this.codecCatalog = codecCatalog;
    }

    public static AndroidDeviceCapabilityProbe system() {
        return new AndroidDeviceCapabilityProbe(new AndroidMediaCodecCatalog());
    }

    public BeaconApiClient.ClientCapabilities read(
        int width,
        int height,
        int refreshHz,
        boolean screenHdr10Supported) {
        int safeRefreshHz = Math.max(1, refreshHz);
        boolean av1 = false;
        boolean hevc = false;
        boolean h264 = false;
        boolean lowLatency = false;
        boolean decoderHdr10 = false;

        for (AndroidCodecDescriptor codec : codecCatalog.codecs()) {
            if (codec.encoder()) {
                continue;
            }

            boolean supportedVideoDecoder = false;
            for (String type : codec.supportedTypes()) {
                String normalized = type == null ? "" : type.trim().toLowerCase(Locale.ROOT);
                if ("video/avc".equals(normalized)) {
                    h264 = true;
                    supportedVideoDecoder = true;
                } else if ("video/hevc".equals(normalized)) {
                    hevc = true;
                    supportedVideoDecoder = true;
                } else if ("video/av01".equals(normalized)) {
                    av1 = true;
                    supportedVideoDecoder = true;
                }
            }

            if (supportedVideoDecoder) {
                lowLatency = lowLatency || codec.lowLatency();
                decoderHdr10 = decoderHdr10 || codec.hdr10();
            }
        }

        return new BeaconApiClient.ClientCapabilities(
            av1,
            hevc,
            h264,
            screenHdr10Supported && decoderHdr10,
            false,
            safeRefreshHz,
            lowLatency,
            width + "x" + height + "@" + safeRefreshHz);
    }
}
