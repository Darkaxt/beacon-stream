package dev.beacon.android;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.net.URI;
import java.util.Arrays;
import java.util.Base64;

public final class BeaconStreamSession {
    private final String host;
    private final String clientId;
    private final int protocolVersion;
    private final String expiresAt;
    private final long planRevision;
    private final String planExplanation;
    private final String sessionId;
    private final int port;
    private final String publicKeyFingerprint;
    private final SelectedVideo selectedVideo;
    private byte[] ticket;
    private boolean ticketConsumed;

    private BeaconStreamSession(
        String host,
        String clientId,
        int protocolVersion,
        byte[] ticket,
        String expiresAt,
        long planRevision,
        String planExplanation,
        String sessionId,
        int port,
        String publicKeyFingerprint,
        SelectedVideo selectedVideo) {
        this.host = host;
        this.clientId = clientId;
        this.protocolVersion = protocolVersion;
        this.ticket = ticket;
        this.expiresAt = expiresAt;
        this.planRevision = planRevision;
        this.planExplanation = planExplanation;
        this.sessionId = sessionId;
        this.port = port;
        this.publicKeyFingerprint = publicKeyFingerprint;
        this.selectedVideo = selectedVideo;
    }

    public static BeaconStreamSession parse(String serverUrl, String clientId, String responseBody) {
        try {
            URI server = URI.create(requireText(serverUrl, "serverUrl"));
            String host = requireText(server.getHost(), "serverUrl host");
            JsonObject root = JsonParser.parseString(requireText(responseBody, "responseBody")).getAsJsonObject();
            if (!root.has("connection") || !root.get("connection").isJsonObject()) {
                throw new IllegalArgumentException("Response must contain a connection grant.");
            }
            JsonObject connection = root.getAsJsonObject("connection");
            int protocolVersion = requiredInt(connection, "protocolVersion");
            if (protocolVersion != 1) {
                throw new IllegalArgumentException("Unsupported Beacon stream protocol version.");
            }
            int port = requiredInt(connection, "port");
            if (port < 1 || port > 65535) {
                throw new IllegalArgumentException("Connection grant port is invalid.");
            }
            String fingerprint = requireText(connection.get("publicKeyFingerprint").getAsString(), "publicKeyFingerprint")
                .replace(":", "").toUpperCase();
            if (!fingerprint.matches("[0-9A-F]{64}")) {
                throw new IllegalArgumentException("publicKeyFingerprint must contain 64 hexadecimal characters.");
            }
            byte[] ticket = Base64.getDecoder().decode(requireText(connection.get("ticket").getAsString(), "ticket"));
            if (ticket.length == 0) {
                throw new IllegalArgumentException("Connection grant ticket is empty.");
            }
            JsonObject video = connection.getAsJsonObject("selectedVideo");
            SelectedVideo selectedVideo = new SelectedVideo(
                requireText(video.get("codec").getAsString(), "selectedVideo.codec"),
                requiredInt(video, "width"),
                requiredInt(video, "height"),
                requiredInt(video, "framesPerSecondNumerator"),
                requiredInt(video, "framesPerSecondDenominator"),
                requireText(video.get("dynamicRange").getAsString(), "selectedVideo.dynamicRange"));
            return new BeaconStreamSession(
                host,
                requireText(clientId, "clientId"),
                protocolVersion,
                ticket,
                requireText(connection.get("expiresAt").getAsString(), "expiresAt"),
                connection.get("planRevision").getAsLong(),
                requireText(connection.get("planExplanation").getAsString(), "planExplanation"),
                requireText(connection.get("sessionId").getAsString(), "sessionId"),
                port,
                fingerprint,
                selectedVideo);
        } catch (IllegalArgumentException error) {
            throw error;
        } catch (RuntimeException error) {
            throw new IllegalArgumentException("Invalid Beacon connection grant.", error);
        }
    }

    public String host() { return host; }
    public int port() { return port; }
    public String sessionId() { return sessionId; }
    public SelectedVideo selectedVideo() { return selectedVideo; }

    public synchronized byte[] consumeTicket() {
        if (ticketConsumed) {
            throw new IllegalStateException("Connection grant ticket was already consumed.");
        }
        byte[] consumed = ticket;
        ticket = new byte[0];
        ticketConsumed = true;
        return consumed;
    }

    public synchronized boolean ticketConsumed() { return ticketConsumed; }

    NativeGrant consumeNativeGrant(long generation) {
        if (generation <= 0) {
            throw new IllegalArgumentException("Connection generation must be positive.");
        }
        return new NativeGrant(
            host, clientId, protocolVersion, consumeTicket(), expiresAt, planRevision,
            planExplanation, sessionId, port, publicKeyFingerprint, selectedVideo, generation);
    }

    private static int requiredInt(JsonObject object, String name) {
        if (!object.has(name)) {
            throw new IllegalArgumentException(name + " is required.");
        }
        int value = object.get(name).getAsInt();
        if (value <= 0) {
            throw new IllegalArgumentException(name + " must be positive.");
        }
        return value;
    }

    private static String requireText(String value, String name) {
        if (value == null || value.trim().isEmpty()) {
            throw new IllegalArgumentException(name + " is required.");
        }
        return value.trim();
    }

    public static final class SelectedVideo {
        private final String codec;
        private final int width;
        private final int height;
        private final int framesPerSecondNumerator;
        private final int framesPerSecondDenominator;
        private final String dynamicRange;

        SelectedVideo(String codec, int width, int height, int fpsNumerator, int fpsDenominator, String dynamicRange) {
            this.codec = codec;
            this.width = width;
            this.height = height;
            this.framesPerSecondNumerator = fpsNumerator;
            this.framesPerSecondDenominator = fpsDenominator;
            this.dynamicRange = dynamicRange;
        }

        public String codec() { return codec; }
        public int width() { return width; }
        public int height() { return height; }
        public int framesPerSecondNumerator() { return framesPerSecondNumerator; }
        public int framesPerSecondDenominator() { return framesPerSecondDenominator; }
        public String dynamicRange() { return dynamicRange; }
    }

    static final class NativeGrant {
        final String host;
        final String clientId;
        final int protocolVersion;
        final byte[] ticket;
        final String expiresAt;
        final long planRevision;
        final String planExplanation;
        final String sessionId;
        final int port;
        final String publicKeyFingerprint;
        final SelectedVideo selectedVideo;
        final long generation;

        NativeGrant(String host, String clientId, int protocolVersion, byte[] ticket, String expiresAt,
                    long planRevision, String planExplanation, String sessionId, int port,
                    String publicKeyFingerprint, SelectedVideo selectedVideo, long generation) {
            this.host = host;
            this.clientId = clientId;
            this.protocolVersion = protocolVersion;
            this.ticket = ticket;
            this.expiresAt = expiresAt;
            this.planRevision = planRevision;
            this.planExplanation = planExplanation;
            this.sessionId = sessionId;
            this.port = port;
            this.publicKeyFingerprint = publicKeyFingerprint;
            this.selectedVideo = selectedVideo;
            this.generation = generation;
        }

        void clearTicket() { Arrays.fill(ticket, (byte) 0); }
    }
}
