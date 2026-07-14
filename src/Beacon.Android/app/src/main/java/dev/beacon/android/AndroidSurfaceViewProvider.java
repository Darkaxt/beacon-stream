package dev.beacon.android;

import android.view.Surface;
import android.view.SurfaceHolder;
import android.view.SurfaceView;

public final class AndroidSurfaceViewProvider implements
    EncodedVideoSurfaceProvider,
    SurfaceHolder.Callback,
    AutoCloseable {
    private final SurfaceHolder holder;
    private Observer observer = Observer.noOp();
    private boolean available;
    private boolean closed;

    public AndroidSurfaceViewProvider(SurfaceView surfaceView) {
        if (surfaceView == null) {
            throw new IllegalArgumentException("Encoded video SurfaceView is required.");
        }
        holder = surfaceView.getHolder();
        holder.addCallback(this);
        available = validSurface() != null;
    }

    @Override
    public synchronized Object currentSurface() {
        return closed ? null : validSurface();
    }

    @Override
    public void setObserver(Observer replacement) {
        if (replacement == null) {
            throw new IllegalArgumentException("Encoded video surface observer is required.");
        }
        boolean notifyAvailable;
        synchronized (this) {
            if (closed) return;
            observer = replacement;
            available = validSurface() != null;
            notifyAvailable = available;
        }
        if (notifyAvailable) replacement.onSurfaceAvailable();
    }

    @Override
    public void surfaceCreated(SurfaceHolder ignored) {
        publishAvailable();
    }

    @Override
    public void surfaceChanged(
        SurfaceHolder ignored,
        int format,
        int width,
        int height) {
        publishAvailable();
    }

    @Override
    public void surfaceDestroyed(SurfaceHolder ignored) {
        Observer current;
        synchronized (this) {
            if (closed || !available) return;
            available = false;
            current = observer;
        }
        current.onSurfaceDestroyed();
    }

    @Override
    public void close() {
        Observer destroyedObserver;
        synchronized (this) {
            if (closed) return;
            closed = true;
            destroyedObserver = available ? observer : null;
            available = false;
            observer = Observer.noOp();
        }
        holder.removeCallback(this);
        if (destroyedObserver != null) destroyedObserver.onSurfaceDestroyed();
    }

    private void publishAvailable() {
        Observer current;
        synchronized (this) {
            if (closed || available || validSurface() == null) return;
            available = true;
            current = observer;
        }
        current.onSurfaceAvailable();
    }

    private Surface validSurface() {
        Surface surface = holder.getSurface();
        return surface != null && surface.isValid() ? surface : null;
    }
}
