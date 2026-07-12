package dev.beacon.android;

import com.google.gson.JsonArray;
import com.google.gson.JsonNull;
import com.google.gson.JsonObject;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class BeaconBenchmarkCompletionRequest {
    private final List<NetworkSample> networkSamples;
    private final List<DecoderSample> decoderSamples;
    private final List<PowerSample> powerSamples;

    private BeaconBenchmarkCompletionRequest(
        List<NetworkSample> networkSamples,
        List<DecoderSample> decoderSamples,
        List<PowerSample> powerSamples) {
        if (networkSamples == null || networkSamples.isEmpty() ||
            decoderSamples == null || decoderSamples.isEmpty() ||
            powerSamples == null || powerSamples.isEmpty()) {
            throw new IllegalArgumentException(
                "Network, decoder, and power benchmark samples are required.");
        }
        this.networkSamples = Collections.unmodifiableList(new ArrayList<>(networkSamples));
        this.decoderSamples = Collections.unmodifiableList(new ArrayList<>(decoderSamples));
        this.powerSamples = Collections.unmodifiableList(new ArrayList<>(powerSamples));
    }

    public static BeaconBenchmarkCompletionRequest fromNetworkResult(
        BeaconStreamCore.BenchmarkNetworkResult network,
        List<DecoderSample> decoderSamples,
        List<PowerSample> powerSamples) {
        if (network == null || network.samples.isEmpty()) {
            throw new IllegalArgumentException("Native benchmark network evidence is required.");
        }
        List<NetworkSample> converted = new ArrayList<>(network.samples.size());
        for (BeaconStreamCore.BenchmarkNetworkSample sample : network.samples) {
            converted.add(new NetworkSample(
                sample.sequence,
                sample.payloadBytes,
                sample.rttUs / 1000.0,
                sample.jitterUs / 1000.0,
                sample.received,
                network.sustainableThroughputMbps,
                sample.reorderDistance));
        }
        return new BeaconBenchmarkCompletionRequest(
            converted,
            decoderSamples,
            powerSamples);
    }

    JsonObject toJson() {
        JsonObject json = new JsonObject();
        JsonArray network = new JsonArray();
        for (NetworkSample sample : networkSamples) network.add(sample.toJson());
        JsonArray decoder = new JsonArray();
        for (DecoderSample sample : decoderSamples) decoder.add(sample.toJson());
        JsonArray power = new JsonArray();
        for (PowerSample sample : powerSamples) power.add(sample.toJson());
        json.add("networkSamples", network);
        json.add("decoderSamples", decoder);
        json.add("powerSamples", power);
        return json;
    }

    private static final class NetworkSample {
        private final long sequence;
        private final int payloadBytes;
        private final double rttMs;
        private final double jitterMs;
        private final boolean received;
        private final double throughputMbps;
        private final int reorderDistance;

        NetworkSample(
            long sequence,
            int payloadBytes,
            double rttMs,
            double jitterMs,
            boolean received,
            double throughputMbps,
            int reorderDistance) {
            this.sequence = sequence;
            this.payloadBytes = payloadBytes;
            this.rttMs = rttMs;
            this.jitterMs = jitterMs;
            this.received = received;
            this.throughputMbps = throughputMbps;
            this.reorderDistance = reorderDistance;
        }

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            json.addProperty("sequence", sequence);
            json.addProperty("payloadBytes", payloadBytes);
            json.addProperty("rttMs", rttMs);
            json.addProperty("jitterMs", jitterMs);
            json.addProperty("received", received);
            json.addProperty("throughputMbps", throughputMbps);
            json.addProperty("reorderDistance", reorderDistance);
            return json;
        }
    }

    public static final class DecoderSample {
        private final String codec;
        private final String profile;
        private final int bitDepth;
        private final int width;
        private final int height;
        private final int targetFps;
        private final boolean configured;
        private final double sustainedFps;
        private final double p95DecodeLatencyMs;
        private final Double p95PresentationLatencyMs;
        private final int droppedFrames;
        private final int outputErrors;
        private final boolean tenBitPresentationVerified;
        private final boolean hdrPresentationVerified;

        public DecoderSample(
            String codec,
            String profile,
            int bitDepth,
            int width,
            int height,
            int targetFps,
            boolean configured,
            double sustainedFps,
            double p95DecodeLatencyMs,
            Double p95PresentationLatencyMs,
            int droppedFrames,
            int outputErrors,
            boolean tenBitPresentationVerified,
            boolean hdrPresentationVerified) {
            this.codec = codec;
            this.profile = profile;
            this.bitDepth = bitDepth;
            this.width = width;
            this.height = height;
            this.targetFps = targetFps;
            this.configured = configured;
            this.sustainedFps = sustainedFps;
            this.p95DecodeLatencyMs = p95DecodeLatencyMs;
            this.p95PresentationLatencyMs = p95PresentationLatencyMs;
            this.droppedFrames = droppedFrames;
            this.outputErrors = outputErrors;
            this.tenBitPresentationVerified = tenBitPresentationVerified;
            this.hdrPresentationVerified = hdrPresentationVerified;
        }

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            json.addProperty("codec", codec);
            json.addProperty("profile", profile);
            json.addProperty("bitDepth", bitDepth);
            json.addProperty("width", width);
            json.addProperty("height", height);
            json.addProperty("targetFps", targetFps);
            json.addProperty("configured", configured);
            json.addProperty("sustainedFps", sustainedFps);
            json.addProperty("p95DecodeLatencyMs", p95DecodeLatencyMs);
            if (p95PresentationLatencyMs == null) {
                json.add("p95PresentationLatencyMs", JsonNull.INSTANCE);
            } else {
                json.addProperty("p95PresentationLatencyMs", p95PresentationLatencyMs);
            }
            json.addProperty("droppedFrames", droppedFrames);
            json.addProperty("outputErrors", outputErrors);
            json.addProperty("tenBitPresentationVerified", tenBitPresentationVerified);
            json.addProperty("hdrPresentationVerified", hdrPresentationVerified);
            return json;
        }
    }

    public static final class PowerSample {
        private final Integer batteryPercent;
        private final boolean isCharging;
        private final String thermalState;

        public PowerSample(Integer batteryPercent, boolean isCharging, String thermalState) {
            this.batteryPercent = batteryPercent;
            this.isCharging = isCharging;
            this.thermalState = thermalState;
        }

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            if (batteryPercent == null) {
                json.add("batteryPercent", JsonNull.INSTANCE);
            } else {
                json.addProperty("batteryPercent", batteryPercent);
            }
            json.addProperty("isCharging", isCharging);
            json.addProperty("thermalState", thermalState);
            return json;
        }
    }
}
