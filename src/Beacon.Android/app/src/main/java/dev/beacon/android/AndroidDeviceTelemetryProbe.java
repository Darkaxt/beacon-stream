package dev.beacon.android;

import android.content.Context;

public final class AndroidDeviceTelemetryProbe {
    private final AndroidDeviceTelemetrySource source;

    public AndroidDeviceTelemetryProbe(AndroidDeviceTelemetrySource source) {
        this.source = source;
    }

    public static AndroidDeviceTelemetryProbe system(Context context) {
        return new AndroidDeviceTelemetryProbe(new AndroidSystemTelemetrySource(context));
    }

    public BeaconApiClient.ClientTelemetry read(
        int rttMs,
        double packetLossPercent,
        int decoderLoadPercent,
        int estimatedBandwidthMbps,
        String fallbackWifiBand,
        int fallbackBatteryPercent,
        String fallbackThermalState) {
        AndroidDeviceTelemetry device = source == null ? AndroidDeviceTelemetry.empty() : source.read();
        if (device == null) {
            device = AndroidDeviceTelemetry.empty();
        }

        return new BeaconApiClient.ClientTelemetry(
            rttMs,
            packetLossPercent,
            decoderLoadPercent,
            estimatedBandwidthMbps,
            chooseString(device.wifiBand(), fallbackWifiBand),
            chooseBattery(device.batteryPercent(), fallbackBatteryPercent),
            chooseString(device.thermalState(), fallbackThermalState));
    }

    private static String chooseString(String primary, String fallback) {
        String normalizedPrimary = normalize(primary);
        if (!normalizedPrimary.isEmpty()) {
            return normalizedPrimary;
        }

        return normalize(fallback);
    }

    private static int chooseBattery(Integer primary, int fallback) {
        if (isValidBattery(primary)) {
            return primary;
        }

        return isValidBattery(fallback) ? fallback : 0;
    }

    private static boolean isValidBattery(Integer value) {
        return value != null && value >= 1 && value <= 100;
    }

    private static String normalize(String value) {
        return value == null ? "" : value.trim();
    }
}
