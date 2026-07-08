package dev.beacon.android;

import android.app.Activity;

public final class AndroidMainThreadDispatcher implements MainThreadDispatcher {
    private final Activity activity;

    public AndroidMainThreadDispatcher(Activity activity) {
        this.activity = activity;
    }

    @Override
    public void dispatch(Runnable action) {
        activity.runOnUiThread(action);
    }
}
