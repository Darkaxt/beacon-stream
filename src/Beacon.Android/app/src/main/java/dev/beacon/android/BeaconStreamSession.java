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
    private final SelectedAudio selectedAudio;
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
        SelectedAudio selectedAudio,
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
        this.selectedAudio = selectedAudio;
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
            SelectedAudio selectedAudio = hasVideo
                ? parseSelectedAudio(connection.getAsJsonObject("selectedAudio"))
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
                selectedAudio,
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
    public SelectedAudio selectedAudio() { return selectedAudio; }
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
            planExplanation, sessionId, port, publicKeyFingerprint, selectedVideo, selectedAudio,
            benchmark, generation);
    }

    private static SelectedVideo parseSelectedVideo(JsonObject video) {
        String dynamicRange = requireText(
            video.get("dynamicRange").getAsString(), "selectedVideo.dynamicRange");
        boolean sdr = "sdr".equalsIgnoreCase(dynamicRange);
        return new SelectedVideo(
            requireText(video.get("codec").getAsString(), "selectedVideo.codec"),
            requiredInt(video, "width"),
            requiredInt(video, "height"),
            requiredInt(video, "framesPerSecondNumerator"),
            requiredInt(video, "framesPerSecondDenominator"),
            dynamicRange,
            optionalText(video, "profile", sdr ? "h264High" : "unspecified"),
            optionalInt(video, "bitDepth", sdr ? 8 : 0),
            optionalText(video, "colorPrimaries", sdr ? "bt709" : "unspecified"),
            optionalText(video, "transferFunction", sdr ? "bt709" : "unspecified"),
            optionalText(video, "matrixCoefficients", sdr ? "bt709" : "unspecified"),
            optionalText(video, "colorRange", sdr ? "limited" : "unspecified"),
            optionalBytes(video, "hdrStaticInfo"),
            optionalBoolean(video, "hdrStaticInfoInBitstream"));
    }

    private static SelectedAudio parseSelectedAudio(JsonObject audio) {
        if (audio == null) {
            throw new IllegalArgumentException("selectedAudio is required for a stream grant.");
        }
        return new SelectedAudio(
            requireText(audio.get("codec").getAsString(), "selectedAudio.codec"),
            requiredInt(audio, "sampleRateHz"),
            requiredInt(audio, "channelCount"),
            requiredInt(audio, "frameDurationUs"),
            requiredInt(audio, "bitrateBps"));
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

    private static int optionalInt(JsonObject object, String name, int fallback) {
        return object.has(name) && !object.get(name).isJsonNull()
            ? object.get(name).getAsInt()
            : fallback;
    }

    private static String optionalText(JsonObject object, String name, String fallback) {
        return object.has(name) && !object.get(name).isJsonNull()
            ? requireText(object.get(name).getAsString(), "selectedVideo." + name)
            : fallback;
    }

    private static byte[] optionalBytes(JsonObject object, String name) {
        if (!object.has(name) || object.get(name).isJsonNull()) return new byte[0];
        String encoded = object.get(name).getAsString();
        return encoded.isEmpty() ? new byte[0] : Base64.getDecoder().decode(encoded);
    }

    private static boolean optionalBoolean(JsonObject object, String name) {
        return object.has(name) && !object.get(name).isJsonNull() &&
            object.get(name).getAsBoolean();
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
        private final String profile;
        private final int bitDepth;
        private final String colorPrimaries;
        private final String transferFunction;
        private final String matrixCoefficients;
        private final String colorRange;
        private final byte[] hdrStaticInfo;
        private final boolean hdrStaticInfoInBitstream;

        SelectedVideo(String codec, int width, int height, int fpsNumerator, int fpsDenominator, String dynamicRange) {
            this(codec, width, height, fpsNumerator, fpsDenominator, dynamicRange,
                "h264High", 8, "bt709", "bt709", "bt709", "limited",
                new byte[0], false);
        }

        SelectedVideo(
            String codec,
            int width,
            int height,
            int fpsNumerator,
            int fpsDenominator,
            String dynamicRange,
            String profile,
            int bitDepth,
            String colorPrimaries,
            String transferFunction,
            String matrixCoefficients,
            String colorRange,
            byte[] hdrStaticInfo,
            boolean hdrStaticInfoInBitstream) {
            this.codec = codec;
            this.width = width;
            this.height = height;
            this.framesPerSecondNumerator = fpsNumerator;
            this.framesPerSecondDenominator = fpsDenominator;
            this.dynamicRange = dynamicRange;
            this.profile = profile;
            this.bitDepth = bitDepth;
            this.colorPrimaries = colorPrimaries;
            this.transferFunction = transferFunction;
            this.matrixCoefficients = matrixCoefficients;
            this.colorRange = colorRange;
            this.hdrStaticInfo = hdrStaticInfo == null
                ? new byte[0]
                : Arrays.copyOf(hdrStaticInfo, hdrStaticInfo.length);
            this.hdrStaticInfoInBitstream = hdrStaticInfoInBitstream;
        }

        public String codec() { return codec; }
        public int width() { return width; }
        public int height() { return height; }
        public int framesPerSecondNumerator() { return framesPerSecondNumerator; }
        public int framesPerSecondDenominator() { return framesPerSecondDenominator; }
        public String dynamicRange() { return dynamicRange; }
        public String profile() { return profile; }
        public int bitDepth() { return bitDepth; }
        public String colorPrimaries() { return colorPrimaries; }
        public String transferFunction() { return transferFunction; }
        public String matrixCoefficients() { return matrixCoefficients; }
        public String colorRange() { return colorRange; }
        public byte[] hdrStaticInfo() { return Arrays.copyOf(hdrStaticInfo, hdrStaticInfo.length); }
        public boolean hdrStaticInfoInBitstream() { return hdrStaticInfoInBitstream; }
    }

    public static final class SelectedAudio {
        private final String codec;
        private final int sampleRateHz;
        private final int channelCount;
        private final int frameDurationUs;
        private final int bitrateBps;

        SelectedAudio(
            String codec,
            int sampleRateHz,
            int channelCount,
            int frameDurationUs,
            int bitrateBps) {
            this.codec = codec;
            this.sampleRateHz = sampleRateHz;
            this.channelCount = channelCount;
            this.frameDurationUs = frameDurationUs;
            this.bitrateBps = bitrateBps;
        }

        public String codec() { return codec; }
        public int sampleRateHz() { return sampleRateHz; }
        public int channelCount() { return channelCount; }
        public int frameDurationUs() { return frameDurationUs; }
        public int bitrateBps() { return bitrateBps; }
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
        final SelectedAudio selectedAudio;
        final Benchmark benchmark;
        final long generation;

        NativeGrant(String host, String clientId, int protocolVersion, byte[] ticket, String expiresAt,
                    long planRevision, String planExplanation, String sessionId, int port,
                    String publicKeyFingerprint, SelectedVideo selectedVideo,
                    SelectedAudio selectedAudio, Benchmark benchmark, long generation) {
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
            this.selectedAudio = selectedAudio;
            this.benchmark = benchmark;
            this.generation = generation;
        }

        void clearSecrets() {
            Arrays.fill(ticket, (byte) 0);
            if (benchmark != null) benchmark.clearRunToken();
        }
    }
}
