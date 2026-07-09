package dev.beacon.android;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

final class GameStreamRtspSdpMetadata {
    private static final GameStreamRtspSdpMetadata Empty = new GameStreamRtspSdpMetadata("");
    private static final String SpropParameterSets = "sprop-parameter-sets";

    private final String h264SpropParameterSets;

    private GameStreamRtspSdpMetadata(String h264SpropParameterSets) {
        this.h264SpropParameterSets = h264SpropParameterSets == null ? "" : h264SpropParameterSets.trim();
    }

    static GameStreamRtspSdpMetadata from(String sdp) {
        if (sdp == null || sdp.trim().isEmpty()) {
            return Empty;
        }

        Set<String> h264PayloadTypes = new HashSet<>();
        List<FmtpParameterSets> videoParameterSets = new ArrayList<>();
        boolean inVideoSection = false;
        String[] lines = sdp.split("\\r?\\n");
        for (String rawLine : lines) {
            String line = rawLine == null ? "" : rawLine.trim();
            if (line.isEmpty()) {
                continue;
            }

            String lowerLine = line.toLowerCase(Locale.ROOT);
            if (lowerLine.startsWith("m=")) {
                inVideoSection = lowerLine.startsWith("m=video");
                continue;
            }

            if (!inVideoSection) {
                continue;
            }

            if (lowerLine.startsWith("a=rtpmap:")) {
                String payloadType = h264PayloadType(line.substring("a=rtpmap:".length()));
                if (!payloadType.isEmpty()) {
                    h264PayloadTypes.add(payloadType);
                }
                continue;
            }

            if (lowerLine.startsWith("a=fmtp:")) {
                FmtpParameterSets parameterSets = fmtpParameterSets(line.substring("a=fmtp:".length()));
                if (parameterSets.present()) {
                    videoParameterSets.add(parameterSets);
                }
            }
        }

        for (FmtpParameterSets parameterSets : videoParameterSets) {
            if (h264PayloadTypes.contains(parameterSets.payloadType())) {
                return new GameStreamRtspSdpMetadata(parameterSets.value());
            }
        }

        if (!videoParameterSets.isEmpty()) {
            return new GameStreamRtspSdpMetadata(videoParameterSets.get(0).value());
        }

        return Empty;
    }

    String h264SpropParameterSets() {
        return h264SpropParameterSets;
    }

    private static String h264PayloadType(String rtpmapValue) {
        String trimmed = rtpmapValue == null ? "" : rtpmapValue.trim();
        int separator = trimmed.indexOf(' ');
        if (separator <= 0) {
            return "";
        }

        String payloadType = trimmed.substring(0, separator).trim();
        String codec = trimmed.substring(separator + 1).trim().toLowerCase(Locale.ROOT);
        if (!payloadType.isEmpty() && (codec.equals("h264") || codec.startsWith("h264/"))) {
            return payloadType;
        }

        return "";
    }

    private static FmtpParameterSets fmtpParameterSets(String fmtpValue) {
        String trimmed = fmtpValue == null ? "" : fmtpValue.trim();
        int separator = trimmed.indexOf(' ');
        if (separator <= 0) {
            return FmtpParameterSets.empty();
        }

        String payloadType = trimmed.substring(0, separator).trim();
        String attributes = trimmed.substring(separator + 1);
        String parameterSets = attributeValue(attributes, SpropParameterSets);
        if (payloadType.isEmpty() || parameterSets.isEmpty()) {
            return FmtpParameterSets.empty();
        }

        return new FmtpParameterSets(payloadType, parameterSets);
    }

    private static String attributeValue(String attributes, String name) {
        String[] parts = attributes == null ? new String[0] : attributes.split(";");
        for (String part : parts) {
            int separator = part.indexOf('=');
            if (separator <= 0) {
                continue;
            }

            String attributeName = part.substring(0, separator).trim().toLowerCase(Locale.ROOT);
            if (name.equals(attributeName)) {
                return part.substring(separator + 1).trim();
            }
        }

        return "";
    }

    private static final class FmtpParameterSets {
        private static final FmtpParameterSets Empty = new FmtpParameterSets("", "");

        private final String payloadType;
        private final String value;

        private FmtpParameterSets(String payloadType, String value) {
            this.payloadType = payloadType == null ? "" : payloadType.trim();
            this.value = value == null ? "" : value.trim();
        }

        private static FmtpParameterSets empty() {
            return Empty;
        }

        private boolean present() {
            return !payloadType.isEmpty() && !value.isEmpty();
        }

        private String payloadType() {
            return payloadType;
        }

        private String value() {
            return value;
        }
    }
}
