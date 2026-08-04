package dev.beacon.android;

public final class AndroidDeviceTelemetry {
    private final Integer batteryPercent;
    private final Boolean isCharging;
    private final String thermalState;
    private final String wifiBand;

    public AndroidDeviceTelemetry(Integer batteryPercent, String thermalState, String wifiBand) {
        this(batteryPercent, null, thermalState, wifiBand);
    }

    public AndroidDeviceTelemetry(
        Integer batteryPercent,
        Boolean isCharging,
        String thermalState,
        String wifiBand) {
        this.batteryPercent = batteryPercent;
        this.isCharging = isCharging;
        this.thermalState = normalize(thermalState);
        this.wifiBand = normalize(wifiBand);
    }

    public static AndroidDeviceTelemetry empty() {
        return new AndroidDeviceTelemetry(null, null, "", "");
    }

    public Integer batteryPercent() {
        return batteryPercent;
    }

    public Boolean isCharging() {
        return isCharging;
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
