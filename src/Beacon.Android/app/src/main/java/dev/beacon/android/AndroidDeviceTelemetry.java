package dev.beacon.android;

public final class AndroidDeviceTelemetry {
    private final Integer batteryPercent;
    private final String thermalState;
    private final String wifiBand;

    public AndroidDeviceTelemetry(Integer batteryPercent, String thermalState, String wifiBand) {
        this.batteryPercent = batteryPercent;
        this.thermalState = normalize(thermalState);
        this.wifiBand = normalize(wifiBand);
    }

    public static AndroidDeviceTelemetry empty() {
        return new AndroidDeviceTelemetry(null, "", "");
    }

    public Integer batteryPercent() {
        return batteryPercent;
    }

    public String thermalState() {
        return thermalState;
    }

    public String wifiBand() {
        return wifiBand;
    }

    private static String normalize(String value) {
        return value == null ? "" : value.trim();
    }
}
