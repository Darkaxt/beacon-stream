package dev.beacon.android;

import android.os.Build;
import android.view.Display;
import android.view.Surface;
import android.view.SurfaceView;

import java.util.Locale;

final class AndroidDisplayBenchmarkPresentationSurfaceFactory
    implements BenchmarkPresentationSurfaceFactory {
    private final SurfaceView surfaceView;
    private final BenchmarkPresentationSurfaceFactory fallback;
    private final HdrWindowModeController windowMode;

    AndroidDisplayBenchmarkPresentationSurfaceFactory(SurfaceView surfaceView) {
        this(
            surfaceView,
            new AndroidImageReaderPresentationSurfaceFactory(),
            HdrWindowModeController.noOp());
    }

    AndroidDisplayBenchmarkPresentationSurfaceFactory(
        SurfaceView surfaceView,
        BenchmarkPresentationSurfaceFactory fallback) {
        this(surfaceView, fallback, HdrWindowModeController.noOp());
    }

    AndroidDisplayBenchmarkPresentationSurfaceFactory(
        SurfaceView surfaceView,
        BenchmarkPresentationSurfaceFactory fallback,
        HdrWindowModeController windowMode) {
        if (surfaceView == null || fallback == null) {
            throw new IllegalArgumentException(
                "Display benchmark surface and fallback are required.");
        }
        this.surfaceView = surfaceView;
        this.fallback = fallback;
        this.windowMode = windowMode;
    }

    @Override
    public BenchmarkPresentationSurface create(int width, int height, Observer observer) {
        return fallback.create(width, height, observer);
    }

    @Override
    public BenchmarkPresentationSurface create(
        EncodedVideoDecodeRequest request,
        Observer observer) {
        Surface surface = surfaceView.getHolder().getSurface();
        if (surface == null || !surface.isValid()) {
            return fallback.create(request, observer);
        }
        boolean hdr = request.isHevcMain10Hdr10();
        windowMode.setHdrEnabled(hdr);
        return new DisplaySurface(surfaceView, surface, windowMode, hdr);
    }

    private static final class DisplaySurface implements BenchmarkPresentationSurface {
        private final SurfaceView surfaceView;
        private final Surface surface;
        private final HdrWindowModeController windowMode;
        private final boolean hdrWindowMode;
        private volatile boolean frameRendered;

        DisplaySurface(
            SurfaceView surfaceView,
            Surface surface,
            HdrWindowModeController windowMode,
            boolean hdrWindowMode) {
            this.surfaceView = surfaceView;
            this.surface = surface;
            this.windowMode = windowMode;
            this.hdrWindowMode = hdrWindowMode;
        }

        @Override public Object surface() { return surface; }

        @Override
        public boolean physicalDisplaySurface() {
            return surface.isValid() && surfaceView.getDisplay() != null && !isEmulator();
        }

        @Override
        public boolean hdr10PresentationVerified() {
            if (!frameRendered || !physicalDisplaySurface() ||
                Build.VERSION.SDK_INT < 34) return false;
            Display display = surfaceView.getDisplay();
            boolean hdr10 = false;
            for (int type : display.getHdrCapabilities().getSupportedHdrTypes()) {
                if (type == Display.HdrCapabilities.HDR_TYPE_HDR10) {
                    hdr10 = true;
                    break;
                }
            }
            return hdr10 && display.isHdrSdrRatioAvailable() &&
                display.getHdrSdrRatio() > 1.0f;
        }

        @Override public void onFrameRendered() { frameRendered = true; }

        @Override public void close() {
            if (hdrWindowMode) windowMode.setHdrEnabled(false);
        }
    }

    static boolean isEmulator() {
        String facts = (Build.FINGERPRINT + " " + Build.MODEL + " " +
            Build.HARDWARE + " " + Build.PRODUCT).toLowerCase(Locale.ROOT);
        return facts.contains("generic") || facts.contains("emulator") ||
            facts.contains("goldfish") || facts.contains("ranchu") ||
            facts.contains("sdk_gphone");
    }
}
