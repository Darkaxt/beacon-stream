package dev.beacon.android;

import android.app.Instrumentation;
import android.content.Intent;

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
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Intent intent = new Intent(
            instrumentation.getTargetContext(),
            BeaconActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        BeaconActivity activity = (BeaconActivity) instrumentation.startActivitySync(intent);
        CountDownLatch available = new CountDownLatch(1);
        CountDownLatch destroyed = new CountDownLatch(1);

        instrumentation.runOnMainSync(() -> {
            AndroidSurfaceViewProvider provider =
                activity.videoSurfaceProviderForInstrumentation();
            assertNotNull(provider);
            provider.setObserver(new EncodedVideoSurfaceProvider.Observer() {
                @Override public void onSurfaceAvailable() { available.countDown(); }
                @Override public void onSurfaceDestroyed() { destroyed.countDown(); }
            });
        });
        available.await();

        instrumentation.runOnMainSync(activity::finish);
        destroyed.await();
    }
}
