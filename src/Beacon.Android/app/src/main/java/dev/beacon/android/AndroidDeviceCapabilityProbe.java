package dev.beacon.android;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
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
        BeaconApiClient.ClientDisplayMode currentDisplayMode,
        List<BeaconApiClient.ClientDisplayMode> supportedDisplayModes,
        boolean screenHdr10Supported) {
        if (currentDisplayMode == null) {
            throw new IllegalArgumentException("Current display mode is required.");
        }
        BeaconApiClient.ClientDisplayMode current = sanitize(currentDisplayMode);
        List<BeaconApiClient.ClientDisplayMode> supported = new ArrayList<>();
        for (BeaconApiClient.ClientDisplayMode mode :
            supportedDisplayModes == null ? Collections.<BeaconApiClient.ClientDisplayMode>emptyList() : supportedDisplayModes) {
            BeaconApiClient.ClientDisplayMode sanitized = sanitize(mode);
            if (!supported.contains(sanitized)) {
                supported.add(sanitized);
            }
        }
        if (!supported.contains(current)) {
            supported.add(current);
        }
        int maxFps = supported.stream().mapToInt(mode -> mode.refreshHz).max().orElse(current.refreshHz);
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
            maxFps,
            lowLatency,
            current,
            supported);
    }

    private static BeaconApiClient.ClientDisplayMode sanitize(BeaconApiClient.ClientDisplayMode mode) {
        if (mode == null || mode.width <= 0 || mode.height <= 0) {
            throw new IllegalArgumentException("Display mode width and height must be positive.");
        }
        return new BeaconApiClient.ClientDisplayMode(
            mode.width,
            mode.height,
            Math.max(1, mode.refreshHz));
    }
}
