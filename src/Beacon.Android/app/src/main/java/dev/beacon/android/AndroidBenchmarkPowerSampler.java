package dev.beacon.android;

final class AndroidBenchmarkPowerSampler implements DeviceBenchmarkPowerSampler {
    private final AndroidDeviceTelemetrySource source;

    AndroidBenchmarkPowerSampler(AndroidDeviceTelemetrySource source) {
        if (source == null) {
            throw new IllegalArgumentException("Android telemetry source is required.");
        }
        this.source = source;
    }

    @Override
    public BeaconBenchmarkCompletionRequest.PowerSample sample() {
        AndroidDeviceTelemetry telemetry = source.read();
        if (telemetry == null) {
            throw new IllegalStateException("Android power telemetry is unavailable.");
        }
        if (telemetry.isCharging() == null) {
            throw new IllegalStateException("Android charging state is unavailable.");
        }
        String thermalState = telemetry.thermalState().isEmpty()
            ? "unknown"
            : telemetry.thermalState();
        return new BeaconBenchmarkCompletionRequest.PowerSample(
            telemetry.batteryPercent(),
            telemetry.isCharging(),
            thermalState);
    }
}
