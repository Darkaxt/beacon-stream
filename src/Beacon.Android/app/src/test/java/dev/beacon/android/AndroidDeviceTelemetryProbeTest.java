package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;

public final class AndroidDeviceTelemetryProbeTest {
    @Test
    public void deviceFactsEnrichManualNetworkSample() {
        AndroidDeviceTelemetryProbe probe = new AndroidDeviceTelemetryProbe(
            new FixedTelemetrySource(new AndroidDeviceTelemetry(64, "moderate", "wifi")));

        BeaconApiClient.ClientTelemetry telemetry = probe.read(
            12,
            0.5,
            27,
            140,
            "",
            0,
            "");

        assertEquals(12, telemetry.rttMs);
        assertEquals(0.5, telemetry.packetLossPercent, 0.001);
        assertEquals(27, telemetry.decoderLoadPercent);
        assertEquals(140, telemetry.estimatedBandwidthMbps);
        assertEquals("wifi", telemetry.wifiBand);
        assertEquals(64, telemetry.batteryPercent);
        assertEquals("moderate", telemetry.thermalState);
    }

    @Test
    public void fallbackValuesAreUsedWhenDeviceFactsAreMissing() {
        AndroidDeviceTelemetryProbe probe = new AndroidDeviceTelemetryProbe(
            new FixedTelemetrySource(AndroidDeviceTelemetry.empty()));

        BeaconApiClient.ClientTelemetry telemetry = probe.read(
            8,
            0,
            20,
            120,
            "wifi-7",
            80,
            "nominal");

        assertEquals("wifi-7", telemetry.wifiBand);
        assertEquals(80, telemetry.batteryPercent);
        assertEquals("nominal", telemetry.thermalState);
    }

    @Test
    public void invalidBatteryAndBlankStringsAreIgnored() {
        AndroidDeviceTelemetryProbe probe = new AndroidDeviceTelemetryProbe(
            new FixedTelemetrySource(new AndroidDeviceTelemetry(-1, " ", "")));

        BeaconApiClient.ClientTelemetry telemetry = probe.read(
            8,
            0,
            20,
            0,
            "",
            0,
            "");

        assertEquals("", telemetry.wifiBand);
        assertEquals(0, telemetry.batteryPercent);
        assertEquals("", telemetry.thermalState);
    }

    @Test
    public void deviceFactsOverrideFallbackValues() {
        AndroidDeviceTelemetryProbe probe = new AndroidDeviceTelemetryProbe(
            new FixedTelemetrySource(new AndroidDeviceTelemetry(35, "hot", "wifi")));

        BeaconApiClient.ClientTelemetry telemetry = probe.read(
            95,
            3.2,
            88,
            80,
            "wifi-5",
            75,
            "nominal");

        assertEquals("wifi", telemetry.wifiBand);
        assertEquals(35, telemetry.batteryPercent);
        assertEquals("hot", telemetry.thermalState);
    }

    private static final class FixedTelemetrySource implements AndroidDeviceTelemetrySource {
        private final AndroidDeviceTelemetry telemetry;

        FixedTelemetrySource(AndroidDeviceTelemetry telemetry) {
            this.telemetry = telemetry;
        }

        @Override
        public AndroidDeviceTelemetry read() {
            return telemetry;
        }
    }
}
