package dev.beacon.android;

import android.app.Instrumentation;
import android.content.Intent;
import android.util.Log;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;

import org.junit.Test;
import org.junit.runner.RunWith;

import java.util.concurrent.CountDownLatch;

import static org.junit.Assert.assertNotNull;

@RunWith(AndroidJUnit4.class)
public final class AndroidSurfaceViewProviderInstrumentationTest {
    @Test
    public void activitySurfacePublishesAvailabilityAndDestruction() throws Exception {
        emit("BEACON_SURFACE_TEST_START");
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Intent intent = new Intent(
            instrumentation.getTargetContext(),
            BeaconActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        BeaconActivity activity = (BeaconActivity) instrumentation.startActivitySync(intent);
        emit("BEACON_SURFACE_ACTIVITY_STARTED");
        CountDownLatch available = new CountDownLatch(1);
        CountDownLatch destroyed = new CountDownLatch(1);

        instrumentation.runOnMainSync(() -> {
            AndroidSurfaceViewProvider provider =
                activity.videoSurfaceProviderForInstrumentation();
            assertNotNull(provider);
            provider.setObserver(new EncodedVideoSurfaceProvider.Observer() {
                @Override public void onSurfaceAvailable() {
                    emit("BEACON_SURFACE_AVAILABLE");
                    available.countDown();
                }

                @Override public void onSurfaceDestroyed() {
                    emit("BEACON_SURFACE_DESTROYED");
                    destroyed.countDown();
                }
            });
        });
        emit("BEACON_SURFACE_AWAIT_AVAILABLE");
        available.await();
        emit("BEACON_SURFACE_AVAILABLE_CONFIRMED");

        emit("BEACON_SURFACE_FINISH_REQUESTED");
        instrumentation.runOnMainSync(activity::finish);
        emit("BEACON_SURFACE_AWAIT_DESTROYED");
        destroyed.await();
        emit("BEACON_SURFACE_TEST_COMPLETE");
    }

    private static void emit(String marker) {
        Log.i("BeaconSurfaceTest", marker);
        System.out.println(marker);
    }
}
