package dev.beacon.android;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.net.URI;
import java.util.Arrays;
import java.util.Base64;
import java.util.UUID;

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
    private final Benchmark benchmark;
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
        SelectedVideo selectedVideo,
        Benchmark benchmark) {
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
        this.benchmark = benchmark;
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
            boolean hasVideo = connection.has("selectedVideo") && connection.get("selectedVideo").isJsonObject();
            boolean hasBenchmark = connection.has("benchmark") && connection.get("benchmark").isJsonObject();
            if (hasVideo == hasBenchmark) {
                throw new IllegalArgumentException(
                    "Connection grant must contain exactly one video or benchmark start mode.");
            }
            SelectedVideo selectedVideo = hasVideo
                ? parseSelectedVideo(connection.getAsJsonObject("selectedVideo"))
                : null;
            Benchmark benchmark = hasBenchmark
                ? parseBenchmark(connection.getAsJsonObject("benchmark"))
                : null;
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
                selectedVideo,
                benchmark);
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
    public Benchmark benchmark() { return benchmark; }

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
            planExplanation, sessionId, port, publicKeyFingerprint, selectedVideo, benchmark, generation);
    }

    private static SelectedVideo parseSelectedVideo(JsonObject video) {
        return new SelectedVideo(
            requireText(video.get("codec").getAsString(), "selectedVideo.codec"),
            requiredInt(video, "width"),
            requiredInt(video, "height"),
            requiredInt(video, "framesPerSecondNumerator"),
            requiredInt(video, "framesPerSecondDenominator"),
            requireText(video.get("dynamicRange").getAsString(), "selectedVideo.dynamicRange"));
    }

    private static Benchmark parseBenchmark(JsonObject value) {
        String runId = UUID.fromString(
            requireText(value.get("runId").getAsString(), "benchmark.runId")).toString();
        byte[] runToken = Base64.getDecoder().decode(
            requireText(value.get("runToken").getAsString(), "benchmark.runToken"));
        if (runToken.length != 16) {
            throw new IllegalArgumentException("benchmark.runToken must contain 16 bytes.");
        }
        return new Benchmark(
            runId,
            requiredInt(value, "schemaVersion"),
            parseBenchmarkRound(value.getAsJsonObject("reliableRound"), "benchmark.reliableRound"),
            parseBenchmarkRound(value.getAsJsonObject("datagramRound"), "benchmark.datagramRound"),
            runToken);
    }

    private static BenchmarkRound parseBenchmarkRound(JsonObject value, String name) {
        if (value == null) {
            throw new IllegalArgumentException(name + " is required.");
        }
        return new BenchmarkRound(
            requiredInt(value, "packetCount"),
            requiredInt(value, "payloadBytes"),
            requiredLong(value, "measurementIntervalUs"));
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

    private static long requiredLong(JsonObject object, String name) {
        if (!object.has(name)) {
            throw new IllegalArgumentException(name + " is required.");
        }
        long value = object.get(name).getAsLong();
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

    public static final class Benchmark {
        private final String runId;
        private final int schemaVersion;
        private final BenchmarkRound reliableRound;
        private final BenchmarkRound datagramRound;
        private final byte[] runToken;

        Benchmark(
            String runId,
            int schemaVersion,
            BenchmarkRound reliableRound,
            BenchmarkRound datagramRound,
            byte[] runToken) {
            this.runId = runId;
            this.schemaVersion = schemaVersion;
            this.reliableRound = reliableRound;
            this.datagramRound = datagramRound;
            this.runToken = runToken;
        }

        public String runId() { return runId; }
        public int schemaVersion() { return schemaVersion; }
        public BenchmarkRound reliableRound() { return reliableRound; }
        public BenchmarkRound datagramRound() { return datagramRound; }
        public byte[] runToken() { return Arrays.copyOf(runToken, runToken.length); }

        void clearRunToken() { Arrays.fill(runToken, (byte) 0); }
    }

    public static final class BenchmarkRound {
        private final int packetCount;
        private final int payloadBytes;
        private final long measurementIntervalUs;

        BenchmarkRound(int packetCount, int payloadBytes, long measurementIntervalUs) {
            this.packetCount = packetCount;
            this.payloadBytes = payloadBytes;
            this.measurementIntervalUs = measurementIntervalUs;
        }

        public int packetCount() { return packetCount; }
        public int payloadBytes() { return payloadBytes; }
        public long measurementIntervalUs() { return measurementIntervalUs; }
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
        final Benchmark benchmark;
        final long generation;

        NativeGrant(String host, String clientId, int protocolVersion, byte[] ticket, String expiresAt,
                    long planRevision, String planExplanation, String sessionId, int port,
                    String publicKeyFingerprint, SelectedVideo selectedVideo,
                    Benchmark benchmark, long generation) {
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
            this.benchmark = benchmark;
            this.generation = generation;
        }

        void clearSecrets() {
            Arrays.fill(ticket, (byte) 0);
            if (benchmark != null) benchmark.clearRunToken();
        }
    }
}
