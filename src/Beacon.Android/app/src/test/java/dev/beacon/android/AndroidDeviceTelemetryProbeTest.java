package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;

public final class AndroidDeviceTelemetryProbeTest {
    @Test
    public void reportsOnlyFactsAvailableFromTheProductionSource() {
        AndroidDeviceTelemetryProbe probe = new AndroidDeviceTelemetryProbe(
            new FixedTelemetrySource(new AndroidDeviceTelemetry(64, "moderate", "6-ghz")));

        BeaconApiClient.ClientTelemetry telemetry = probe.read();

        assertNull(telemetry.rttMs);
        assertNull(telemetry.packetLossPercent);
        assertNull(telemetry.decoderLoadPercent);
        assertNull(telemetry.estimatedBandwidthMbps);
        assertEquals("6-ghz", telemetry.wifiBand);
        assertEquals(Integer.valueOf(64), telemetry.batteryPercent);
        assertEquals("moderate", telemetry.thermalState);
    }

    @Test
    public void missingDeviceFactsRemainUnknown() {
        AndroidDeviceTelemetryProbe probe = new AndroidDeviceTelemetryProbe(
            new FixedTelemetrySource(AndroidDeviceTelemetry.empty()));

        BeaconApiClient.ClientTelemetry telemetry = probe.read();

        assertEquals("", telemetry.wifiBand);
        assertNull(telemetry.batteryPercent);
        assertEquals("", telemetry.thermalState);
    }

    @Test
    public void systemTelemetryMapsOnlyObservedWifiFrequenciesToBands() {
        assertEquals("2.4-ghz", AndroidSystemTelemetrySource.wifiBand(2412));
        assertEquals("5-ghz", AndroidSystemTelemetrySource.wifiBand(5745));
        assertEquals("6-ghz", AndroidSystemTelemetrySource.wifiBand(6135));
        assertEquals("", AndroidSystemTelemetrySource.wifiBand(0));
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
