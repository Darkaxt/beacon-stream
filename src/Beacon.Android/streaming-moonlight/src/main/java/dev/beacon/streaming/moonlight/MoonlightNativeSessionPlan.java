package dev.beacon.streaming.moonlight;

import java.net.URI;
import java.net.URISyntaxException;
import java.util.Base64;
import java.util.Locale;

public final class MoonlightNativeSessionPlan {
    public static final int STREAM_CFG_LOCAL = 0;
    public static final int STREAM_CFG_REMOTE = 1;
    public static final int STREAM_CFG_AUTO = 2;

    public static final int COLORSPACE_REC_601 = 0;
    public static final int COLORSPACE_REC_709 = 1;
    public static final int COLORSPACE_REC_2020 = 2;

    public static final int COLOR_RANGE_LIMITED = 0;
    public static final int COLOR_RANGE_FULL = 1;

    public static final int ENCFLG_NONE = 0x00000000;
    public static final int ENCFLG_AUDIO = 0x00000001;
    public static final int ENCFLG_VIDEO = 0x00000002;
    public static final int ENCFLG_ALL = 0xFFFFFFFF;

    public static final int AUDIO_CONFIGURATION_STEREO = makeAudioConfiguration(2, 0x3);
    public static final int AUDIO_CONFIGURATION_51_SURROUND = makeAudioConfiguration(6, 0x3F);
    public static final int AUDIO_CONFIGURATION_71_SURROUND = makeAudioConfiguration(8, 0x63F);

    public static final int VIDEO_FORMAT_H264 = 0x0001;
    public static final int VIDEO_FORMAT_H264_HIGH8_444 = 0x0004;
    public static final int VIDEO_FORMAT_H265 = 0x0100;
    public static final int VIDEO_FORMAT_H265_MAIN10 = 0x0200;
    public static final int VIDEO_FORMAT_H265_REXT8_444 = 0x0400;
    public static final int VIDEO_FORMAT_H265_REXT10_444 = 0x0800;
    public static final int VIDEO_FORMAT_AV1_MAIN8 = 0x1000;
    public static final int VIDEO_FORMAT_AV1_MAIN10 = 0x2000;
    public static final int VIDEO_FORMAT_AV1_HIGH8_444 = 0x4000;
    public static final int VIDEO_FORMAT_AV1_HIGH10_444 = 0x8000;

    private static final int AES_BLOCK_SIZE = 16;

    private final String address;
    private final String serverAppVersion;
    private final String serverGfeVersion;
    private final String rtspSessionUrl;
    private final int serverCodecModeSupport;
    private final int width;
    private final int height;
    private final int fps;
    private final int bitrateKbps;
    private final int packetSize;
    private final int streamingRemotely;
    private final int audioConfiguration;
    private final int supportedVideoFormats;
    private final int clientRefreshRateX100;
    private final int colorSpace;
    private final int colorRange;
    private final int encryptionFlags;
    private final byte[] remoteInputAesKey;
    private final byte[] remoteInputAesIv;

    private MoonlightNativeSessionPlan(
        String address,
        String serverAppVersion,
        String serverGfeVersion,
        String rtspSessionUrl,
        int serverCodecModeSupport,
        int width,
        int height,
        int fps,
        int bitrateKbps,
        int packetSize,
        int streamingRemotely,
        int audioConfiguration,
        int supportedVideoFormats,
        int clientRefreshRateX100,
        int colorSpace,
        int colorRange,
        int encryptionFlags,
        byte[] remoteInputAesKey,
        byte[] remoteInputAesIv) {
        this.address = address;
        this.serverAppVersion = serverAppVersion;
        this.serverGfeVersion = serverGfeVersion;
        this.rtspSessionUrl = rtspSessionUrl;
        this.serverCodecModeSupport = serverCodecModeSupport;
        this.width = width;
        this.height = height;
        this.fps = fps;
        this.bitrateKbps = bitrateKbps;
        this.packetSize = packetSize;
        this.streamingRemotely = streamingRemotely;
        this.audioConfiguration = audioConfiguration;
        this.supportedVideoFormats = supportedVideoFormats;
        this.clientRefreshRateX100 = clientRefreshRateX100;
        this.colorSpace = colorSpace;
        this.colorRange = colorRange;
        this.encryptionFlags = encryptionFlags;
        this.remoteInputAesKey = remoteInputAesKey.clone();
        this.remoteInputAesIv = remoteInputAesIv.clone();
    }

    public static MoonlightNativeSessionPlan create(
        String address,
        String serverAppVersion,
        String serverGfeVersion,
        String rtspSessionUrl,
        int serverCodecModeSupport,
        int width,
        int height,
        int fps,
        int bitrateKbps,
        int packetSize,
        String streamingMode,
        String audioConfiguration,
        String videoFormat,
        int clientRefreshRateX100,
        String colorSpace,
        String colorRange,
        String encryptionMode,
        String remoteInputAesKey,
        String remoteInputAesIv) {
        String requiredAddress = requireText(address, "address");
        String requiredAppVersion = requireText(serverAppVersion, "serverAppVersion");
        String requiredRtspUrl = requireRtspUrl(rtspSessionUrl);
        requirePositive(serverCodecModeSupport, "serverCodecModeSupport");
        requirePositive(width, "width");
        requirePositive(height, "height");
        requirePositive(fps, "fps");
        requirePositive(bitrateKbps, "bitrateKbps");
        if (packetSize <= 0 || packetSize > 0xFFFF) {
            throw invalid("packetSize", "must be between 1 and 65535");
        }
        requirePositive(clientRefreshRateX100, "clientRefreshRateX100");

        return new MoonlightNativeSessionPlan(
            requiredAddress,
            requiredAppVersion,
            optionalText(serverGfeVersion),
            requiredRtspUrl,
            serverCodecModeSupport,
            width,
            height,
            fps,
            bitrateKbps,
            packetSize,
            mapStreamingMode(streamingMode),
            mapAudioConfiguration(audioConfiguration),
            mapVideoFormat(videoFormat),
            clientRefreshRateX100,
            mapColorSpace(colorSpace),
            mapColorRange(colorRange),
            mapEncryptionMode(encryptionMode),
            decodeAesMaterial(remoteInputAesKey, "remoteInputAesKey"),
            decodeAesMaterial(remoteInputAesIv, "remoteInputAesIv"));
    }

    public String address() {
        return address;
    }

    public String serverAppVersion() {
        return serverAppVersion;
    }

    public String serverGfeVersion() {
        return serverGfeVersion;
    }

    public String rtspSessionUrl() {
        return rtspSessionUrl;
    }

    public int serverCodecModeSupport() {
        return serverCodecModeSupport;
    }

    public int width() {
        return width;
    }

    public int height() {
        return height;
    }

    public int fps() {
        return fps;
    }

    public int bitrateKbps() {
        return bitrateKbps;
    }

    public int packetSize() {
        return packetSize;
    }

    public int streamingRemotely() {
        return streamingRemotely;
    }

    public int audioConfiguration() {
        return audioConfiguration;
    }

    public int supportedVideoFormats() {
        return supportedVideoFormats;
    }

    public int clientRefreshRateX100() {
        return clientRefreshRateX100;
    }

    public int colorSpace() {
        return colorSpace;
    }

    public int colorRange() {
        return colorRange;
    }

    public int encryptionFlags() {
        return encryptionFlags;
    }

    public byte[] remoteInputAesKey() {
        return remoteInputAesKey.clone();
    }

    public byte[] remoteInputAesIv() {
        return remoteInputAesIv.clone();
    }

    @Override
    public String toString() {
        return "MoonlightNativeSessionPlan{" +
            "address='" + address + '\'' +
            ", rtspSessionUrl='" + rtspSessionUrl + '\'' +
            ", video=" + width + 'x' + height + '@' + fps +
            ", remoteInputAesKey=[redacted]" +
            ", remoteInputAesIv=[redacted]}";
    }

    private static int makeAudioConfiguration(int channelCount, int channelMask) {
        return (channelMask << 16) | (channelCount << 8) | 0xCA;
    }

    private static int mapStreamingMode(String value) {
        return switch (canonical(value, "streamingMode")) {
            case "local" -> STREAM_CFG_LOCAL;
            case "remote" -> STREAM_CFG_REMOTE;
            case "auto" -> STREAM_CFG_AUTO;
            default -> throw invalid("streamingMode", "must be local, remote, or auto");
        };
    }

    private static int mapAudioConfiguration(String value) {
        return switch (canonical(value, "audioConfiguration")) {
            case "stereo" -> AUDIO_CONFIGURATION_STEREO;
            case "5.1" -> AUDIO_CONFIGURATION_51_SURROUND;
            case "7.1" -> AUDIO_CONFIGURATION_71_SURROUND;
            default -> throw invalid("audioConfiguration", "must be stereo, 5.1, or 7.1");
        };
    }

    private static int mapVideoFormat(String value) {
        return switch (canonical(value, "videoFormat")) {
            case "h264" -> VIDEO_FORMAT_H264;
            case "h264-high8-444" -> VIDEO_FORMAT_H264_HIGH8_444;
            case "hevc" -> VIDEO_FORMAT_H265;
            case "hevc-main10" -> VIDEO_FORMAT_H265_MAIN10;
            case "hevc-rext8-444" -> VIDEO_FORMAT_H265_REXT8_444;
            case "hevc-rext10-444" -> VIDEO_FORMAT_H265_REXT10_444;
            case "av1-main8" -> VIDEO_FORMAT_AV1_MAIN8;
            case "av1-main10" -> VIDEO_FORMAT_AV1_MAIN10;
            case "av1-high8-444" -> VIDEO_FORMAT_AV1_HIGH8_444;
            case "av1-high10-444" -> VIDEO_FORMAT_AV1_HIGH10_444;
            default -> throw invalid("videoFormat", "is not supported");
        };
    }

    private static int mapColorSpace(String value) {
        return switch (canonical(value, "colorSpace")) {
            case "rec601" -> COLORSPACE_REC_601;
            case "rec709" -> COLORSPACE_REC_709;
            case "rec2020" -> COLORSPACE_REC_2020;
            default -> throw invalid("colorSpace", "must be rec601, rec709, or rec2020");
        };
    }

    private static int mapColorRange(String value) {
        return switch (canonical(value, "colorRange")) {
            case "limited" -> COLOR_RANGE_LIMITED;
            case "full" -> COLOR_RANGE_FULL;
            default -> throw invalid("colorRange", "must be limited or full");
        };
    }

    private static int mapEncryptionMode(String value) {
        return switch (canonical(value, "encryptionMode")) {
            case "none" -> ENCFLG_NONE;
            case "audio" -> ENCFLG_AUDIO;
            case "video" -> ENCFLG_VIDEO;
            case "all" -> ENCFLG_ALL;
            default -> throw invalid("encryptionMode", "must be none, audio, video, or all");
        };
    }

    private static String requireRtspUrl(String value) {
        String normalized = requireText(value, "rtspSessionUrl");
        try {
            URI uri = new URI(normalized);
            if (!"rtsp".equalsIgnoreCase(uri.getScheme()) || uri.getHost() == null || uri.getHost().isBlank()) {
                throw invalid("rtspSessionUrl", "must be an absolute rtsp:// URI with a host");
            }
            return normalized;
        } catch (URISyntaxException ex) {
            throw invalid("rtspSessionUrl", "must be an absolute rtsp:// URI with a host");
        }
    }

    private static byte[] decodeAesMaterial(String value, String field) {
        String encoded = requireText(value, field);
        try {
            byte[] decoded = Base64.getDecoder().decode(encoded);
            if (decoded.length != AES_BLOCK_SIZE) {
                throw invalid(field, "must decode to exactly 16 bytes");
            }
            return decoded;
        } catch (IllegalArgumentException ex) {
            if (ex.getMessage() != null && ex.getMessage().startsWith(field + " ")) {
                throw ex;
            }
            throw invalid(field, "must be valid Base64 that decodes to exactly 16 bytes");
        }
    }

    private static void requirePositive(int value, String field) {
        if (value <= 0) {
            throw invalid(field, "must be positive");
        }
    }

    private static String canonical(String value, String field) {
        return requireText(value, field).toLowerCase(Locale.ROOT);
    }

    private static String requireText(String value, String field) {
        String normalized = optionalText(value);
        if (normalized == null) {
            throw invalid(field, "is required");
        }
        return normalized;
    }

    private static String optionalText(String value) {
        if (value == null) {
            return null;
        }
        String normalized = value.trim();
        return normalized.isEmpty() ? null : normalized;
    }

    private static IllegalArgumentException invalid(String field, String message) {
        return new IllegalArgumentException(field + " " + message + '.');
    }
}
