package dev.beacon.android;

import android.media.MediaCodecInfo;
import android.media.MediaCodecList;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public final class AndroidMediaCodecCatalog implements AndroidCodecCatalog {
    private static final String LowLatencyFeature = "low-latency";

    @Override
    public List<AndroidCodecDescriptor> codecs() {
        List<AndroidCodecDescriptor> result = new ArrayList<>();
        MediaCodecInfo[] codecInfos = new MediaCodecList(MediaCodecList.ALL_CODECS).getCodecInfos();
        for (MediaCodecInfo codecInfo : codecInfos) {
            String[] types = codecInfo.getSupportedTypes();
            boolean lowLatency = false;
            boolean hdr10 = false;
            for (String type : types) {
                MediaCodecInfo.CodecCapabilities capabilities = capabilitiesFor(codecInfo, type);
                if (capabilities == null) {
                    continue;
                }

                lowLatency = lowLatency || supportsLowLatency(capabilities);
                hdr10 = hdr10 || supportsHdr10(type, capabilities);
            }

            result.add(new AndroidCodecDescriptor(codecInfo.isEncoder(), types, lowLatency, hdr10));
        }

        return result;
    }

    private static MediaCodecInfo.CodecCapabilities capabilitiesFor(MediaCodecInfo codecInfo, String type) {
        try {
            return codecInfo.getCapabilitiesForType(type);
        } catch (RuntimeException ex) {
            return null;
        }
    }

    private static boolean supportsLowLatency(MediaCodecInfo.CodecCapabilities capabilities) {
        try {
            return capabilities.isFeatureSupported(LowLatencyFeature);
        } catch (RuntimeException ex) {
            return false;
        }
    }

    private static boolean supportsHdr10(String type, MediaCodecInfo.CodecCapabilities capabilities) {
        String normalizedType = type == null ? "" : type.trim().toLowerCase(Locale.ROOT);
        if (!"video/hevc".equals(normalizedType) && !"video/av01".equals(normalizedType)) {
            return false;
        }

        for (MediaCodecInfo.CodecProfileLevel profileLevel : capabilities.profileLevels) {
            if (isHevcHdr10Profile(normalizedType, profileLevel.profile) ||
                isAv1Hdr10Profile(normalizedType, profileLevel.profile)) {
                return true;
            }
        }

        return false;
    }

    static boolean isHevcHdr10Profile(String type, int profile) {
        return "video/hevc".equals(type) &&
            (profile == MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10 ||
                profile == MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10Plus);
    }

    private static boolean isAv1Hdr10Profile(String type, int profile) {
        return "video/av01".equals(type) &&
            (profile == MediaCodecInfo.CodecProfileLevel.AV1ProfileMain10HDR10 ||
                profile == MediaCodecInfo.CodecProfileLevel.AV1ProfileMain10HDR10Plus);
    }
}
