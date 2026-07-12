package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertThrows;

public final class AndroidBenchmarkPowerSamplerTest {
    @Test
    public void reportsBatteryChargingAndThermalFactsFromOneSnapshot() {
        AndroidBenchmarkPowerSampler sampler = new AndroidBenchmarkPowerSampler(
            () -> new AndroidDeviceTelemetry(81, true, "moderate", "wifi"));

        String json = sampler.sample().toJson().toString();

        assertEquals(
            "{\"batteryPercent\":81,\"isCharging\":true,\"thermalState\":\"moderate\"}",
            json);
    }

    @Test
    public void unavailableChargingStateFailsInsteadOfInventingUnpluggedState() {
        AndroidBenchmarkPowerSampler sampler = new AndroidBenchmarkPowerSampler(
            () -> new AndroidDeviceTelemetry(81, null, "moderate", "wifi"));

        IllegalStateException error = assertThrows(
            IllegalStateException.class,
            sampler::sample);

        assertEquals("Android charging state is unavailable.", error.getMessage());
    }
}
