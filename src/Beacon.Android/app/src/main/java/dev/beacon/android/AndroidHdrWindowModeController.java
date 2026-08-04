package dev.beacon.android;

import android.app.Activity;
import android.content.pm.ActivityInfo;
import android.os.Looper;
import android.view.Window;

import java.util.concurrent.ExecutionException;
import java.util.concurrent.FutureTask;

final class AndroidHdrWindowModeController implements HdrWindowModeController {
    private final Activity activity;
    private boolean enabled;
    private int restoreColorMode = ActivityInfo.COLOR_MODE_DEFAULT;

    AndroidHdrWindowModeController(Activity activity) {
        if (activity == null) throw new IllegalArgumentException("Activity is required.");
        this.activity = activity;
    }

    @Override
    public synchronized void setHdrEnabled(boolean requested) {
        if (enabled == requested) return;
        runOnMainThread(() -> {
            Window window = activity.getWindow();
            if (requested) {
                restoreColorMode = window.getColorMode();
                window.setColorMode(ActivityInfo.COLOR_MODE_HDR);
            } else {
                window.setColorMode(restoreColorMode);
            }
        });
        enabled = requested;
    }

    private void runOnMainThread(Runnable action) {
        if (Looper.myLooper() == Looper.getMainLooper()) {
            action.run();
            return;
        }
        FutureTask<Void> task = new FutureTask<>(action, null);
        activity.runOnUiThread(task);
        try {
            task.get();
        } catch (InterruptedException failure) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("Interrupted while changing HDR window mode.", failure);
        } catch (ExecutionException failure) {
            throw new IllegalStateException("Could not change HDR window mode.", failure.getCause());
        }
    }
}
