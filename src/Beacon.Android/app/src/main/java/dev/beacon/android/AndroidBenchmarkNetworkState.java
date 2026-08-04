package dev.beacon.android;

final class AndroidBenchmarkNetworkState {
    private final String transport;
    private final String localNetworkPrefix;
    private final String wifiBand;
    private final Integer wifiChannel;
    private final String linkSpeedBucket;
    private final String rawSsid;
    private final String rawBssid;

    AndroidBenchmarkNetworkState(
        String transport,
        String localNetworkPrefix,
        String wifiBand,
        Integer wifiChannel,
        String linkSpeedBucket,
        String rawSsid,
        String rawBssid) {
        this.transport = transport;
        this.localNetworkPrefix = localNetworkPrefix;
        this.wifiBand = wifiBand;
        this.wifiChannel = wifiChannel;
        this.linkSpeedBucket = linkSpeedBucket;
        this.rawSsid = rawSsid;
        this.rawBssid = rawBssid;
    }

    static AndroidBenchmarkNetworkState disconnected() {
        return new AndroidBenchmarkNetworkState(
            "none", "0.0.0.0/0", null, null, "unknown", null, null);
    }

    String transport() { return transport; }
    String localNetworkPrefix() { return localNetworkPrefix; }
    String wifiBand() { return wifiBand; }
    Integer wifiChannel() { return wifiChannel; }
    String linkSpeedBucket() { return linkSpeedBucket; }
    String rawSsid() { return rawSsid; }
    String rawBssid() { return rawBssid; }
}
