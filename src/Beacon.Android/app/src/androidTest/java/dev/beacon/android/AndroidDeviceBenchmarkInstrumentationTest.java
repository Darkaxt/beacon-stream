package dev.beacon.android;

import android.content.Context;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;

import com.google.gson.JsonParser;

import org.junit.Test;
import org.junit.runner.RunWith;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;

@RunWith(AndroidJUnit4.class)
public final class AndroidDeviceBenchmarkInstrumentationTest {
    @Test
    public void packagedH264VectorProducesDecodePresentationAndPowerEvidence()
        throws InterruptedException {
        Context context = InstrumentationRegistry.getInstrumentation().getTargetContext();
        BeaconDeviceBenchmarkRunner runner = AndroidDeviceBenchmarkRunner.system(context);
        CountDownLatch completed = new CountDownLatch(1);
        AtomicReference<BeaconBenchmarkDeviceEvidence> evidence = new AtomicReference<>();
        AtomicReference<Throwable> failure = new AtomicReference<>();

        runner.start(plan(), new BeaconDeviceBenchmarkRunner.Observer() {
            @Override
            public void onCompleted(BeaconBenchmarkDeviceEvidence value) {
                evidence.set(value);
                completed.countDown();
            }

            @Override
            public void onFailure(Throwable value) {
                failure.set(value);
                completed.countDown();
            }
        });

        completed.await();

        if (failure.get() != null) {
            throw new AssertionError("Android device benchmark failed.", failure.get());
        }
        assertNotNull("Android device benchmark returned no evidence.", evidence.get());
        assertEquals("Unexpected decoder evidence: " + evidence.get().decoderSamples(),
            1, evidence.get().decoderSamples().size());
        assertEquals("Unexpected power evidence: " + evidence.get().powerSamples(),
            2, evidence.get().powerSamples().size());
        String decoder = evidence.get().decoderSamples().get(0).toJson().toString();
        System.out.println("BEACON_HARDWARE_EVIDENCE " + decoder);
        assertTrue("Decoder was not configured: " + decoder,
            decoder.contains("\"configured\":true"));
        assertTrue("Presentation latency is missing: " + decoder,
            decoder.contains("\"p95PresentationLatencyMs\":"));
        assertTrue("Dropped-frame evidence is missing: " + decoder,
            decoder.contains("\"droppedFrames\":"));
        assertTrue("Decoder reported output errors: " + decoder,
            decoder.contains("\"outputErrors\":0"));
    }

    private static BeaconBenchmarkHardwarePlan plan() {
        return BeaconBenchmarkHardwarePlan.parse(JsonParser.parseString(
            "{\"schemaVersion\":1,\"samplePowerBeforeAndAfterEachRound\":true," +
                "\"decoderRounds\":[{\"vectorId\":\"beacon-h264-high-8-1280x720-60-v1\"," +
                "\"codec\":\"h264\",\"profile\":\"high\",\"bitDepth\":8," +
                "\"width\":1280,\"height\":720,\"targetFps\":60,\"repetitionCount\":3}]}")
            .getAsJsonObject());
    }
}
