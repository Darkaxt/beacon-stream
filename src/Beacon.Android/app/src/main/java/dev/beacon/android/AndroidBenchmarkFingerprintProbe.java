package dev.beacon.android;

import java.net.URI;

final class AndroidBenchmarkFingerprintProbe {
    private static final int SCHEMA_VERSION = 3;
    private final BeaconNetworkIdentityHasher identityHasher;
    private final HardwareSource hardwareSource;

    AndroidBenchmarkFingerprintProbe(
        BeaconNetworkIdentityHasher identityHasher,
        HardwareSource hardwareSource) {
        if (identityHasher == null || hardwareSource == null) {
            throw new IllegalArgumentException("Benchmark fingerprint dependencies are required.");
        }
        this.identityHasher = identityHasher;
        this.hardwareSource = hardwareSource;
    }

    BeaconBenchmarkPrepareRequest create(
        String trigger,
        String serverUrl,
        BeaconApiClient.ClientCapabilities capabilities,
        AndroidBenchmarkNetworkState network) {
        if (capabilities == null) {
            throw new IllegalArgumentException("Client capabilities are required.");
        }
        URI server = URI.create(serverUrl == null ? "" : serverUrl.trim());
        String serverRoute = server.getHost();
        if (serverRoute == null || serverRoute.isBlank()) {
            throw new IllegalArgumentException("Benchmark server route is required.");
        }
        AndroidBenchmarkNetworkState current = network == null
            ? AndroidBenchmarkNetworkState.disconnected()
            : network;
        HardwareFacts hardware = hardwareSource.read(capabilities);
        return new BeaconBenchmarkPrepareRequest(
            trigger,
            new BeaconBenchmarkPrepareRequest.FingerprintSet(
                BeaconBenchmarkPrepareRequest.NetworkFingerprint.fromLocalNetwork(
                    SCHEMA_VERSION,
                    serverRoute,
                    current.transport(),
                    current.localNetworkPrefix(),
                    current.wifiBand(),
                    current.wifiChannel(),
                    current.linkSpeedBucket(),
                    current.rawSsid(),
                    current.rawBssid(),
                    identityHasher),
                new BeaconBenchmarkPrepareRequest.HardwareFingerprint(
                    SCHEMA_VERSION,
                    hardware.deviceCapabilityRevision,
                    hardware.androidVersion,
                    hardware.apkVersion,
                    hardware.displayModeInventoryRevision,
                    hardware.codecInventoryRevision)));
    }

    static String wifiBand(int frequencyMhz) {
        if (frequencyMhz >= 2400 && frequencyMhz < 2500) return "2.4-ghz";
        if (frequencyMhz >= 4900 && frequencyMhz < 5925) return "5-ghz";
        if (frequencyMhz >= 5925 && frequencyMhz < 7125) return "6-ghz";
        if (frequencyMhz >= 57_000 && frequencyMhz < 71_000) return "60-ghz";
        return null;
    }

    static Integer wifiChannel(int frequencyMhz) {
        if (frequencyMhz == 2484) return 14;
        if (frequencyMhz >= 2412 && frequencyMhz <= 2472) {
            return (frequencyMhz - 2407) / 5;
        }
        if (frequencyMhz >= 5000 && frequencyMhz < 5925) {
            return (frequencyMhz - 5000) / 5;
        }
        if (frequencyMhz >= 5955 && frequencyMhz < 7125) {
            return (frequencyMhz - 5950) / 5;
        }
        return null;
    }

    static String linkSpeedBucket(int megabitsPerSecond) {
        if (megabitsPerSecond <= 0) return "unknown";
        if (megabitsPerSecond < 100) return "under-100-mbps";
        if (megabitsPerSecond < 500) return "100-499-mbps";
        if (megabitsPerSecond < 1000) return "500-999-mbps";
        return "1000+-mbps";
    }

    interface HardwareSource {
        HardwareFacts read(BeaconApiClient.ClientCapabilities capabilities);
    }

    static final class HardwareFacts {
        final String deviceCapabilityRevision;
        final String androidVersion;
        final String apkVersion;
        final String displayModeInventoryRevision;
        final String codecInventoryRevision;

        HardwareFacts(
            String deviceCapabilityRevision,
            String androidVersion,
            String apkVersion,
            String displayModeInventoryRevision,
            String codecInventoryRevision) {
            this.deviceCapabilityRevision = deviceCapabilityRevision;
            this.androidVersion = androidVersion;
            this.apkVersion = apkVersion;
            this.displayModeInventoryRevision = displayModeInventoryRevision;
            this.codecInventoryRevision = codecInventoryRevision;
        }
    }
}
