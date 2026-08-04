package dev.beacon.android;

import android.content.Context;
import android.app.Activity;
import android.view.SurfaceView;

public final class AndroidDeviceBenchmarkRunner implements BeaconDeviceBenchmarkRunner {
    private final BeaconDeviceBenchmarkRunner delegate;

    private AndroidDeviceBenchmarkRunner(BeaconDeviceBenchmarkRunner delegate) {
        this.delegate = delegate;
    }

    public static AndroidDeviceBenchmarkRunner system(Context context) {
        return system(context, new AndroidImageReaderPresentationSurfaceFactory());
    }

    public static AndroidDeviceBenchmarkRunner system(
        Context context,
        SurfaceView surfaceView) {
        HdrWindowModeController windowMode = context instanceof Activity activity
            ? new AndroidHdrWindowModeController(activity)
            : HdrWindowModeController.noOp();
        return system(
            context,
            new AndroidDisplayBenchmarkPresentationSurfaceFactory(
                surfaceView,
                new AndroidImageReaderPresentationSurfaceFactory(),
                windowMode));
    }

    private static AndroidDeviceBenchmarkRunner system(
        Context context,
        BenchmarkPresentationSurfaceFactory surfaces) {
        AndroidSystemTelemetrySource telemetry = new AndroidSystemTelemetrySource(context);
        return new AndroidDeviceBenchmarkRunner(new SequentialDeviceBenchmarkRunner(
            new MediaCodecDeviceBenchmarkRoundExecutor(
                new AndroidMediaCodecFactory(),
                new AndroidAssetBenchmarkVectorRepository(context),
                surfaces),
            new AndroidBenchmarkPowerSampler(telemetry)));
    }

    @Override
    public Run start(BeaconBenchmarkHardwarePlan plan, Observer observer) {
        return delegate.start(plan, observer);
    }
}
