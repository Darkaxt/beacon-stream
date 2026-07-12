package dev.beacon.android;

import android.content.Context;

public final class AndroidDeviceBenchmarkRunner implements BeaconDeviceBenchmarkRunner {
    private final BeaconDeviceBenchmarkRunner delegate;

    private AndroidDeviceBenchmarkRunner(BeaconDeviceBenchmarkRunner delegate) {
        this.delegate = delegate;
    }

    public static AndroidDeviceBenchmarkRunner system(Context context) {
        AndroidSystemTelemetrySource telemetry = new AndroidSystemTelemetrySource(context);
        return new AndroidDeviceBenchmarkRunner(new SequentialDeviceBenchmarkRunner(
            new MediaCodecDeviceBenchmarkRoundExecutor(
                new AndroidMediaCodecFactory(),
                new AndroidAssetBenchmarkVectorRepository(context),
                new AndroidImageReaderPresentationSurfaceFactory()),
            new AndroidBenchmarkPowerSampler(telemetry)));
    }

    @Override
    public Run start(BeaconBenchmarkHardwarePlan plan, Observer observer) {
        return delegate.start(plan, observer);
    }
}
