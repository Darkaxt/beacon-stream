package dev.beacon.android;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class BeaconBenchmarkHardwarePlan {
    private static final int SUPPORTED_SCHEMA_VERSION = 1;

    private final int schemaVersion;
    private final List<DecoderRound> decoderRounds;
    private final boolean samplePowerBeforeAndAfterEachRound;

    private BeaconBenchmarkHardwarePlan(
        int schemaVersion,
        List<DecoderRound> decoderRounds,
        boolean samplePowerBeforeAndAfterEachRound) {
        this.schemaVersion = schemaVersion;
        this.decoderRounds = Collections.unmodifiableList(new ArrayList<>(decoderRounds));
        this.samplePowerBeforeAndAfterEachRound = samplePowerBeforeAndAfterEachRound;
    }

    static BeaconBenchmarkHardwarePlan parse(JsonObject value) {
        if (value == null || value.get("schemaVersion").getAsInt() != SUPPORTED_SCHEMA_VERSION) {
            throw new IllegalArgumentException("Unsupported benchmark hardware-plan schema.");
        }
        JsonElement roundsValue = value.get("decoderRounds");
        if (roundsValue == null || !roundsValue.isJsonArray()) {
            throw new IllegalArgumentException("Benchmark decoder rounds are required.");
        }
        List<DecoderRound> rounds = new ArrayList<>();
        JsonArray array = roundsValue.getAsJsonArray();
        for (JsonElement element : array) {
            if (!element.isJsonObject()) {
                throw new IllegalArgumentException("Benchmark decoder round is invalid.");
            }
            rounds.add(DecoderRound.parse(element.getAsJsonObject()));
        }
        return new BeaconBenchmarkHardwarePlan(
            SUPPORTED_SCHEMA_VERSION,
            rounds,
            value.get("samplePowerBeforeAndAfterEachRound").getAsBoolean());
    }

    public int schemaVersion() { return schemaVersion; }
    public List<DecoderRound> decoderRounds() { return decoderRounds; }
    public boolean samplePowerBeforeAndAfterEachRound() {
        return samplePowerBeforeAndAfterEachRound;
    }

    public static final class DecoderRound {
        private final String vectorId;
        private final String codec;
        private final String profile;
        private final int bitDepth;
        private final int width;
        private final int height;
        private final int targetFps;
        private final int repetitionCount;

        private DecoderRound(
            String vectorId,
            String codec,
            String profile,
            int bitDepth,
            int width,
            int height,
            int targetFps,
            int repetitionCount) {
            this.vectorId = requireText(vectorId, "vectorId");
            this.codec = requireText(codec, "codec");
            this.profile = requireText(profile, "profile");
            if (bitDepth != 8 && bitDepth != 10) {
                throw new IllegalArgumentException("Benchmark decoder bit depth is invalid.");
            }
            if (width <= 0 || height <= 0 || targetFps <= 0 || repetitionCount <= 0) {
                throw new IllegalArgumentException("Benchmark decoder dimensions and counts must be positive.");
            }
            this.bitDepth = bitDepth;
            this.width = width;
            this.height = height;
            this.targetFps = targetFps;
            this.repetitionCount = repetitionCount;
        }

        static DecoderRound parse(JsonObject value) {
            return new DecoderRound(
                value.get("vectorId").getAsString(),
                value.get("codec").getAsString(),
                value.get("profile").getAsString(),
                value.get("bitDepth").getAsInt(),
                value.get("width").getAsInt(),
                value.get("height").getAsInt(),
                value.get("targetFps").getAsInt(),
                value.get("repetitionCount").getAsInt());
        }

        public String vectorId() { return vectorId; }
        public String codec() { return codec; }
        public String profile() { return profile; }
        public int bitDepth() { return bitDepth; }
        public int width() { return width; }
        public int height() { return height; }
        public int targetFps() { return targetFps; }
        public int repetitionCount() { return repetitionCount; }
    }

    private static String requireText(String value, String name) {
        if (value == null || value.trim().isEmpty()) {
            throw new IllegalArgumentException("Benchmark " + name + " is required.");
        }
        return value.trim();
    }
}
