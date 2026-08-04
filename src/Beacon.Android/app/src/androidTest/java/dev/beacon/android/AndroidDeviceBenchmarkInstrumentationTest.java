package dev.beacon.android;

import android.content.Context;
import android.media.MediaCodecList;
import android.media.MediaFormat;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import org.junit.Test;
import org.junit.Assume;
import org.junit.runner.RunWith;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicReference;
import java.security.MessageDigest;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;
import static org.junit.Assert.assertFalse;

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
        JsonObject decoder = evidence.get().decoderSamples().get(0).toJson();
        System.out.println("BEACON_HARDWARE_EVIDENCE " + decoder);
        assertTrue("Decoder was not configured: " + decoder,
            decoder.has("configured") && decoder.get("configured").getAsBoolean());
        assertTrue("Presentation latency is missing: " + decoder,
            decoder.has("p95PresentationLatencyMs"));
        assertTrue("Dropped-frame evidence is missing: " + decoder,
            decoder.has("droppedFrames"));
        assertTrue("Output-error evidence is missing: " + decoder,
            decoder.has("outputErrors"));
        assertTrue("Output errors are not a numeric measurement: " + decoder,
            decoder.get("outputErrors").isJsonPrimitive() &&
                decoder.getAsJsonPrimitive("outputErrors").isNumber());
        assertTrue("Output errors are negative: " + decoder,
            decoder.get("outputErrors").getAsInt() >= 0);
    }

    @Test
    public void packagedHevcMain10Hdr10VectorDecodesButCannotCertifyEmulatorPresentation()
        throws Exception {
        Context context = InstrumentationRegistry.getInstrumentation().getTargetContext();
        AndroidAssetBenchmarkVectorRepository vectors =
            new AndroidAssetBenchmarkVectorRepository(context);
        byte[] vector = vectors.load("beacon-hevc-main10-hdr10-320x180-30-v1");
        assertEquals(
            "5ec0b193c717c4b96f3350a0e45d83d847a9ba27c1b9cd24b6f958f9817508b1",
            hex(MessageDigest.getInstance("SHA-256").digest(vector)));

        BeaconBenchmarkHardwarePlan plan = hdrPlan();
        BeaconBenchmarkHardwarePlan.DecoderRound round = plan.decoderRounds().get(0);
        EncodedVideoDecodeRequest request = BenchmarkEncodedVideoRequestFactory.create(
            round,
            new EncodedVideoSampleProvider() {
                @Override public EncodedVideoSample nextSample() { return EncodedVideoSample.eos(); }
                @Override public int maxSampleBytes() { return 0; }
            });
        MediaFormat format = AndroidMediaCodecFactory.buildMediaFormat(request, 0, false);
        String decoderName = new MediaCodecList(MediaCodecList.REGULAR_CODECS)
            .findDecoderForFormat(format);
        Assume.assumeTrue(
            "Emulator has no exact HEVC Main10 HDR10 decoder.",
            decoderName != null && !decoderName.isBlank());

        BeaconDeviceBenchmarkRunner runner = AndroidDeviceBenchmarkRunner.system(context);
        CountDownLatch completed = new CountDownLatch(1);
        AtomicReference<BeaconBenchmarkDeviceEvidence> evidence = new AtomicReference<>();
        AtomicReference<Throwable> failure = new AtomicReference<>();
        runner.start(plan, new BeaconDeviceBenchmarkRunner.Observer() {
            @Override public void onCompleted(BeaconBenchmarkDeviceEvidence value) {
                evidence.set(value);
                completed.countDown();
            }
            @Override public void onFailure(Throwable value) {
                failure.set(value);
                completed.countDown();
            }
        });
        completed.await();

        if (failure.get() != null) {
            throw new AssertionError("HEVC HDR10 emulator decode failed.", failure.get());
        }
        JsonObject decoder = evidence.get().decoderSamples().get(0).toJson();
        System.out.println("BEACON_HEVC_HDR10_EMULATOR_EVIDENCE decoder=" +
            decoderName + " sample=" + decoder);
        assertTrue(decoder.get("configured").getAsBoolean());
        assertEquals(0, decoder.get("outputErrors").getAsInt());
        assertFalse(decoder.get("tenBitPresentationVerified").getAsBoolean());
        assertFalse(decoder.get("hdrPresentationVerified").getAsBoolean());
    }

    private static BeaconBenchmarkHardwarePlan plan() {
        return BeaconBenchmarkHardwarePlan.parse(JsonParser.parseString(
            "{\"schemaVersion\":1,\"samplePowerBeforeAndAfterEachRound\":true," +
                "\"decoderRounds\":[{\"vectorId\":\"beacon-h264-high-8-1280x720-60-v1\"," +
                "\"codec\":\"h264\",\"profile\":\"high\",\"bitDepth\":8," +
                "\"width\":1280,\"height\":720,\"targetFps\":60,\"repetitionCount\":3}]}")
            .getAsJsonObject());
    }

    private static BeaconBenchmarkHardwarePlan hdrPlan() {
        return BeaconBenchmarkHardwarePlan.parse(JsonParser.parseString(
            "{\"schemaVersion\":1,\"samplePowerBeforeAndAfterEachRound\":true," +
                "\"decoderRounds\":[{\"vectorId\":\"beacon-hevc-main10-hdr10-320x180-30-v1\"," +
                "\"codec\":\"hevc\",\"profile\":\"main10\",\"bitDepth\":10," +
                "\"width\":320,\"height\":180,\"targetFps\":30,\"repetitionCount\":1}]}" )
            .getAsJsonObject());
    }

    private static String hex(byte[] bytes) {
        StringBuilder result = new StringBuilder(bytes.length * 2);
        for (byte value : bytes) result.append(String.format("%02x", value & 0xff));
        return result.toString();
    }
}
