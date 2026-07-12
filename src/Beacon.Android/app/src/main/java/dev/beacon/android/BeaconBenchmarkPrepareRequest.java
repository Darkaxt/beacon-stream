package dev.beacon.android;

import com.google.gson.JsonNull;
import com.google.gson.JsonObject;

public final class BeaconBenchmarkPrepareRequest {
    private static final int SUPPORTED_SCHEMA_VERSION = 3;

    private final String trigger;
    private final FingerprintSet fingerprints;

    public BeaconBenchmarkPrepareRequest(String trigger, FingerprintSet fingerprints) {
        this.trigger = requireText(trigger, "trigger");
        if (!this.trigger.equals("automatic") && !this.trigger.equals("manual") &&
            !this.trigger.equals("sessionPreflight")) {
            throw new IllegalArgumentException(
                "trigger must be automatic, manual, or sessionPreflight.");
        }
        if (fingerprints == null) {
            throw new IllegalArgumentException("fingerprints is required.");
        }
        this.fingerprints = fingerprints;
    }

    public FingerprintSet fingerprints() {
        return fingerprints;
    }

    public String serialize() {
        return BeaconJson.gson().toJson(toJson());
    }

    JsonObject toJson() {
        JsonObject json = new JsonObject();
        json.addProperty("trigger", trigger);
        json.add("fingerprints", fingerprints.toJson());
        return json;
    }

    @Override
    public String toString() {
        return serialize();
    }

    public static final class FingerprintSet {
        private final NetworkFingerprint network;
        private final HardwareFingerprint hardware;

        public FingerprintSet(NetworkFingerprint network, HardwareFingerprint hardware) {
            if (network == null || hardware == null) {
                throw new IllegalArgumentException("Network and hardware fingerprints are required.");
            }
            this.network = network;
            this.hardware = hardware;
        }

        public NetworkFingerprint network() {
            return network;
        }

        public HardwareFingerprint hardware() {
            return hardware;
        }

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            json.add("network", network.toJson());
            json.add("hardware", hardware.toJson());
            return json;
        }

        @Override
        public String toString() {
            return BeaconJson.gson().toJson(toJson());
        }
    }

    public static final class NetworkFingerprint {
        private final int schemaVersion;
        private final String serverRoute;
        private final String transport;
        private final String localNetworkPrefix;
        private final String wifiBand;
        private final Integer wifiChannel;
        private final String linkSpeedBucket;
        private final String saltedNetworkIdHash;

        private NetworkFingerprint(
            int schemaVersion,
            String serverRoute,
            String transport,
            String localNetworkPrefix,
            String wifiBand,
            Integer wifiChannel,
            String linkSpeedBucket,
            String saltedNetworkIdHash) {
            if (schemaVersion != SUPPORTED_SCHEMA_VERSION) {
                throw new IllegalArgumentException("schemaVersion must be 3.");
            }
            this.schemaVersion = schemaVersion;
            this.serverRoute = requireText(serverRoute, "serverRoute");
            this.transport = requireText(transport, "transport");
            this.localNetworkPrefix = requireText(localNetworkPrefix, "localNetworkPrefix");
            this.wifiBand = optionalText(wifiBand);
            this.wifiChannel = wifiChannel;
            this.linkSpeedBucket = requireText(linkSpeedBucket, "linkSpeedBucket");
            this.saltedNetworkIdHash = optionalText(saltedNetworkIdHash);
        }

        public static NetworkFingerprint fromLocalNetwork(
            int schemaVersion,
            String serverRoute,
            String transport,
            String localNetworkPrefix,
            String wifiBand,
            Integer wifiChannel,
            String linkSpeedBucket,
            String rawSsid,
            String rawBssid,
            BeaconNetworkIdentityHasher identityHasher) {
            if (identityHasher == null) {
                throw new IllegalArgumentException("identityHasher is required.");
            }
            return new NetworkFingerprint(
                schemaVersion,
                serverRoute,
                transport,
                localNetworkPrefix,
                wifiBand,
                wifiChannel,
                linkSpeedBucket,
                identityHasher.hash(rawSsid, rawBssid));
        }

        public String saltedNetworkIdHash() {
            return saltedNetworkIdHash;
        }

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            json.addProperty("schemaVersion", schemaVersion);
            json.addProperty("serverRoute", serverRoute);
            json.addProperty("transport", transport);
            json.addProperty("localNetworkPrefix", localNetworkPrefix);
            addNullable(json, "wifiBand", wifiBand);
            addNullable(json, "wifiChannel", wifiChannel);
            json.addProperty("linkSpeedBucket", linkSpeedBucket);
            addNullable(json, "saltedNetworkIdHash", saltedNetworkIdHash);
            return json;
        }

        @Override
        public String toString() {
            return BeaconJson.gson().toJson(toJson());
        }
    }

    public static final class HardwareFingerprint {
        private final int schemaVersion;
        private final String deviceCapabilityRevision;
        private final String androidVersion;
        private final String apkVersion;
        private final String displayModeInventoryRevision;
        private final String codecInventoryRevision;

        public HardwareFingerprint(
            int schemaVersion,
            String deviceCapabilityRevision,
            String androidVersion,
            String apkVersion,
            String displayModeInventoryRevision,
            String codecInventoryRevision) {
            if (schemaVersion != SUPPORTED_SCHEMA_VERSION) {
                throw new IllegalArgumentException("schemaVersion must be 3.");
            }
            this.schemaVersion = schemaVersion;
            this.deviceCapabilityRevision = requireText(
                deviceCapabilityRevision,
                "deviceCapabilityRevision");
            this.androidVersion = requireText(androidVersion, "androidVersion");
            this.apkVersion = requireText(apkVersion, "apkVersion");
            this.displayModeInventoryRevision = requireText(
                displayModeInventoryRevision,
                "displayModeInventoryRevision");
            this.codecInventoryRevision = requireText(codecInventoryRevision, "codecInventoryRevision");
        }

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            json.addProperty("schemaVersion", schemaVersion);
            json.addProperty("deviceCapabilityRevision", deviceCapabilityRevision);
            json.addProperty("androidVersion", androidVersion);
            json.addProperty("apkVersion", apkVersion);
            json.addProperty("displayModeInventoryRevision", displayModeInventoryRevision);
            json.addProperty("codecInventoryRevision", codecInventoryRevision);
            return json;
        }

        @Override
        public String toString() {
            return BeaconJson.gson().toJson(toJson());
        }
    }

    private static String requireText(String value, String name) {
        String normalized = optionalText(value);
        if (normalized == null) {
            throw new IllegalArgumentException(name + " is required.");
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

    private static void addNullable(JsonObject json, String name, String value) {
        if (value == null) {
            json.add(name, JsonNull.INSTANCE);
        } else {
            json.addProperty(name, value);
        }
    }

    private static void addNullable(JsonObject json, String name, Integer value) {
        if (value == null) {
            json.add(name, JsonNull.INSTANCE);
        } else {
            json.addProperty(name, value);
        }
    }
}
