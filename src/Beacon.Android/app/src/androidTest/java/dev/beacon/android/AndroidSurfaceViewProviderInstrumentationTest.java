package dev.beacon.android;

import android.app.Instrumentation;
import android.content.Intent;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.runner.lifecycle.ActivityLifecycleCallback;
import androidx.test.runner.lifecycle.ActivityLifecycleMonitor;
import androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry;
import androidx.test.runner.lifecycle.Stage;

import org.junit.Test;
import org.junit.runner.RunWith;

import java.util.concurrent.CountDownLatch;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;

@RunWith(AndroidJUnit4.class)
public final class AndroidSurfaceViewProviderInstrumentationTest {
    @Test
    public void activitySurfacePublishesAvailabilityAndDestruction() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Intent intent = new Intent(
            instrumentation.getTargetContext(),
            BeaconActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        BeaconActivity activity = (BeaconActivity) instrumentation.startActivitySync(intent);
        CountDownLatch available = new CountDownLatch(1);
        CountDownLatch surfaceDestroyed = new CountDownLatch(1);
        CountDownLatch activityDestroyed = new CountDownLatch(1);
        ActivityLifecycleMonitor monitor = ActivityLifecycleMonitorRegistry.getInstance();
        ActivityLifecycleCallback callback = (candidate, stage) -> {
            if (candidate == activity && stage == Stage.DESTROYED) {
                activityDestroyed.countDown();
            }
        };
        monitor.addLifecycleCallback(callback);

        try {
            instrumentation.waitForIdleSync();
            instrumentation.runOnMainSync(() -> {
                AndroidSurfaceViewProvider provider =
                    activity.videoSurfaceProviderForInstrumentation();
                assertNotNull(provider);
                provider.setObserver(new EncodedVideoSurfaceProvider.Observer() {
                    @Override public void onSurfaceAvailable() {
                        available.countDown();
                    }

                    @Override public void onSurfaceDestroyed() {
                        surfaceDestroyed.countDown();
                    }
                });
            });
            instrumentation.waitForIdleSync();
            assertEquals("The activity stream surface was not available.", 0, available.getCount());
        } finally {
            instrumentation.runOnMainSync(activity::finish);
            activityDestroyed.await();
            monitor.removeLifecycleCallback(callback);
        }

        assertEquals(
            "Destroying the activity did not destroy the stream surface.",
            0,
            surfaceDestroyed.getCount());
    }
}
