package dev.beacon.android;

import android.view.Surface;
import android.view.SurfaceView;

public final class AndroidSurfaceViewProvider implements EncodedVideoSurfaceProvider {
    private final SurfaceView surfaceView;

    public AndroidSurfaceViewProvider(SurfaceView surfaceView) {
        this.surfaceView = surfaceView;
    }

    @Override
    public Object currentSurface() {
        if (surfaceView == null || surfaceView.getHolder() == null) {
            return null;
        }

        Surface surface = surfaceView.getHolder().getSurface();
        return surface != null && surface.isValid() ? surface : null;
    }
}
