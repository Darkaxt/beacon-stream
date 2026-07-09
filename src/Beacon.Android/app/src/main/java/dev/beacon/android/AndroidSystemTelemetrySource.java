package dev.beacon.android;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.BatteryManager;
import android.os.Build;
import android.os.PowerManager;

public final class AndroidSystemTelemetrySource implements AndroidDeviceTelemetrySource {
    private final Context context;

    public AndroidSystemTelemetrySource(Context context) {
        this.context = context.getApplicationContext();
    }

    @Override
    public AndroidDeviceTelemetry read() {
        return new AndroidDeviceTelemetry(readBatteryPercent(), readThermalState(), readWifiBand());
    }

    private Integer readBatteryPercent() {
        BatteryManager batteryManager = (BatteryManager) context.getSystemService(Context.BATTERY_SERVICE);
        if (batteryManager == null) {
            return null;
        }

        int capacity = batteryManager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY);
        return capacity >= 1 && capacity <= 100 ? capacity : null;
    }

    private String readThermalState() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) {
            return "";
        }

        PowerManager powerManager = (PowerManager) context.getSystemService(Context.POWER_SERVICE);
        if (powerManager == null) {
            return "";
        }

        return switch (powerManager.getCurrentThermalStatus()) {
            case PowerManager.THERMAL_STATUS_NONE -> "none";
            case PowerManager.THERMAL_STATUS_LIGHT -> "light";
            case PowerManager.THERMAL_STATUS_MODERATE -> "moderate";
            case PowerManager.THERMAL_STATUS_SEVERE -> "hot";
            case PowerManager.THERMAL_STATUS_CRITICAL,
                PowerManager.THERMAL_STATUS_EMERGENCY,
                PowerManager.THERMAL_STATUS_SHUTDOWN -> "critical";
            default -> "";
        };
    }

    private String readWifiBand() {
        ConnectivityManager connectivityManager =
            (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
        if (connectivityManager == null) {
            return "";
        }

        try {
            Network network = connectivityManager.getActiveNetwork();
            if (network == null) {
                return "";
            }

            NetworkCapabilities capabilities = connectivityManager.getNetworkCapabilities(network);
            if (capabilities != null && capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) {
                return "wifi";
            }
        } catch (SecurityException ex) {
            return "";
        }

        return "";
    }
}
