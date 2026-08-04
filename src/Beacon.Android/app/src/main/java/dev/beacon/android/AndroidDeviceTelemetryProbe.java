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

    public BeaconApiClient.ClientTelemetry read() {
        AndroidDeviceTelemetry device = source == null ? AndroidDeviceTelemetry.empty() : source.read();
        if (device == null) {
            device = AndroidDeviceTelemetry.empty();
        }

        return new BeaconApiClient.ClientTelemetry(
            null,
            null,
            null,
            null,
            normalize(device.wifiBand()),
            validBattery(device.batteryPercent()),
            normalize(device.thermalState()));
    }

    private static Integer validBattery(Integer value) {
        return value != null && value >= 1 && value <= 100 ? value : null;
    }

    private static String normalize(String value) {
        return value == null ? "" : value.trim();
    }
}
