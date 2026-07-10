package dev.beacon.android;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public final class EncodedVideoStreamPlan {
    private static final String StreamKind = "encoded-video";

    private final boolean supportedProtocol;
    private final String videoUri;
    private final String sampleTransport;
    private final String sampleUri;
    private final String codec;
    private final String container;
    private final int width;
    private final int height;
    private final int fps;
    private final String diagnostic;

    private EncodedVideoStreamPlan(
        boolean supportedProtocol,
        String videoUri,
        String sampleTransport,
        String sampleUri,
        String codec,
        String container,
        int width,
        int height,
        int fps,
        String diagnostic) {
        this.supportedProtocol = supportedProtocol;
        this.videoUri = videoUri;
        this.sampleTransport = sampleTransport;
        this.sampleUri = sampleUri;
        this.codec = codec;
        this.container = container;
        this.width = width;
        this.height = height;
        this.fps = fps;
        this.diagnostic = diagnostic;
    }

    public static EncodedVideoStreamPlan from(StreamConnectionDescriptor connection) {
        if (connection == null ||
            !connection.present() ||
            !"beacon-test".equalsIgnoreCase(connection.protocol()) ||
            !StreamKind.equalsIgnoreCase(connection.metadataValue("streamKind"))) {
            return new EncodedVideoStreamPlan(false, "", "", "", "", "", 0, 0, 0, "");
        }

        String videoUri = videoUri(connection);
        String sampleTransport = normalize(connection.metadataValue("sampleTransport"));
        String sampleUri = sampleUri(connection, sampleTransport);
        String codec = normalize(connection.metadataValue("codec"));
        String container = normalize(connection.metadataValue("container"));
        int width = positiveInteger(connection.metadataValue("width"));
        int height = positiveInteger(connection.metadataValue("height"));
        int fps = positiveInteger(connection.metadataValue("fps"));

        List<String> missing = new ArrayList<>();
        if (videoUri.isEmpty()) {
            missing.add("video endpoint");
        }
        if (!sampleTransport.isEmpty() && sampleUri.isEmpty()) {
            missing.add("samples endpoint");
        }
        if (!validCodec(codec)) {
            missing.add("codec");
        }
        if (!validContainer(container)) {
            missing.add("container");
        }
        if (width <= 0) {
            missing.add("width");
        }
        if (height <= 0) {
            missing.add("height");
        }
        if (fps <= 0) {
            missing.add("fps");
        }

        String diagnostic = missing.isEmpty()
            ? ""
            : "Encoded video stream contract is incomplete. Missing or invalid: " + String.join(", ", missing) + ".";

        return new EncodedVideoStreamPlan(
            true,
            videoUri,
            validSampleTransport(sampleTransport) ? sampleTransport : "",
            validSampleTransport(sampleTransport) ? sampleUri : "",
            validCodec(codec) ? codec : "",
            validContainer(container) ? container : "",
            width,
            height,
            fps,
            diagnostic);
    }

    public static EncodedVideoStreamPlan fromGameStreamRtp(GameStreamEndpointPlan plan) {
        if (plan == null || !plan.supportedProtocol()) {
            return new EncodedVideoStreamPlan(false, "", "", "", "", "", 0, 0, 0, "");
        }

        String videoUri = plan.videoUri();
        String codec = normalize(plan.metadataValue("codec"));
        String container = normalize(plan.metadataValue("container"));
        int width = positiveInteger(plan.metadataValue("width"));
        int height = positiveInteger(plan.metadataValue("height"));
        int fps = positiveInteger(plan.metadataValue("fps"));

        List<String> missing = new ArrayList<>();
        if (!validCodec(codec)) {
            missing.add("codec");
        }
        if (!validContainer(container)) {
            missing.add("container");
        }
        if (width <= 0) {
            missing.add("width");
        }
        if (height <= 0) {
            missing.add("height");
        }
        if (fps <= 0) {
            missing.add("fps");
        }

        String diagnostic = missing.isEmpty()
            ? ""
            : "GameStream RTP video metadata is incomplete. Missing or invalid: " + String.join(", ", missing) + ".";

        return new EncodedVideoStreamPlan(
            true,
            videoUri,
            "",
            "",
            validCodec(codec) ? codec : "",
            validContainer(container) ? container : "",
            width,
            height,
            fps,
            diagnostic);
    }

    public static EncodedVideoStreamPlan nativeMoonlight(String codec, int width, int height, int fps) {
        String normalizedCodec = normalize(codec);
        if (!validCodec(normalizedCodec)) {
            throw new IllegalArgumentException("Unsupported native Moonlight codec '" + codec + "'.");
        }
        if (width <= 0 || height <= 0 || fps <= 0) {
            throw new IllegalArgumentException("Native Moonlight video dimensions and frame rate must be positive.");
        }

        return new EncodedVideoStreamPlan(
            true,
            "moonlight-native://video",
            "",
            "",
            normalizedCodec,
            "annex-b",
            width,
            height,
            fps,
            "");
    }

    public boolean supportedProtocol() {
        return supportedProtocol;
    }

    public boolean complete() {
        return supportedProtocol && diagnostic.isEmpty();
    }

    public String videoUri() {
        return videoUri;
    }

    public String sampleTransport() {
        return sampleTransport;
    }

    public String sampleUri() {
        return sampleUri;
    }

    public String codec() {
        return codec;
    }

    public String container() {
        return container;
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

    public String diagnostic() {
        return diagnostic;
    }

    private static String videoUri(StreamConnectionDescriptor connection) {
        return endpointUri(connection, "video");
    }

    private static String sampleUri(StreamConnectionDescriptor connection, String sampleTransport) {
        return "beacon-annexb-samples".equals(sampleTransport) ? endpointUri(connection, "samples") : "";
    }

    private static String endpointUri(StreamConnectionDescriptor connection, String role) {
        for (StreamConnectionDescriptor.Endpoint endpoint : connection.endpoints()) {
            if (role.equalsIgnoreCase(endpoint.role())) {
                return endpoint.uri();
            }
        }

        return "";
    }

    private static boolean validSampleTransport(String sampleTransport) {
        return sampleTransport.isEmpty() || "beacon-annexb-samples".equals(sampleTransport);
    }

    private static boolean validCodec(String codec) {
        return "h264".equals(codec) || "hevc".equals(codec) || "av1".equals(codec);
    }

    private static boolean validContainer(String container) {
        return "annex-b".equals(container) || "mp4".equals(container);
    }

    private static int positiveInteger(String value) {
        try {
            int parsed = Integer.parseInt(value);
            return parsed > 0 ? parsed : 0;
        } catch (NumberFormatException ex) {
            return 0;
        }
    }

    private static String normalize(String value) {
        return value == null ? "" : value.trim().toLowerCase(Locale.ROOT);
    }
}
