package dev.beacon.android;

import com.google.gson.JsonParser;

import org.junit.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class SequentialDeviceBenchmarkRunnerTest {
    @Test
    public void executesServerRoundsInOrderAndSamplesPowerAroundEachRound() {
        RecordingRoundExecutor executor = new RecordingRoundExecutor();
        AtomicInteger battery = new AtomicInteger(90);
        SequentialDeviceBenchmarkRunner runner = new SequentialDeviceBenchmarkRunner(
            executor,
            () -> new BeaconBenchmarkCompletionRequest.PowerSample(
                battery.getAndDecrement(), false, "nominal"));
        RecordingObserver observer = new RecordingObserver();

        BeaconDeviceBenchmarkRunner.Run run = runner.start(twoRoundPlan(), observer);
        assertEquals("vector-a", executor.startedVectorIds.get(0));
        executor.complete(decoderSample("h264"));
        assertEquals("vector-b", executor.startedVectorIds.get(1));
        executor.complete(decoderSample("hevc"));

        assertTrue(observer.completed);
        assertEquals(2, observer.evidence.decoderSamples().size());
        assertEquals(4, observer.evidence.powerSamples().size());
        assertEquals(0, executor.cancelCount);
        run.cancel();
        assertEquals(0, executor.cancelCount);
    }

    @Test
    public void cancellationStopsOnlyTheActiveRoundAndSuppressesLateCallbacks() {
        RecordingRoundExecutor executor = new RecordingRoundExecutor();
        SequentialDeviceBenchmarkRunner runner = new SequentialDeviceBenchmarkRunner(
            executor,
            () -> new BeaconBenchmarkCompletionRequest.PowerSample(80, false, "nominal"));
        RecordingObserver observer = new RecordingObserver();

        BeaconDeviceBenchmarkRunner.Run run = runner.start(twoRoundPlan(), observer);
        run.cancel();
        executor.complete(decoderSample("h264"));

        assertEquals(1, executor.cancelCount);
        assertFalse(observer.completed);
        assertEquals(1, executor.startedVectorIds.size());
    }

    @Test
    public void roundInfrastructureFailureEndsRunWithoutStartingAnotherRound() {
        RecordingRoundExecutor executor = new RecordingRoundExecutor();
        SequentialDeviceBenchmarkRunner runner = new SequentialDeviceBenchmarkRunner(
            executor,
            () -> new BeaconBenchmarkCompletionRequest.PowerSample(80, false, "nominal"));
        RecordingObserver observer = new RecordingObserver();

        runner.start(twoRoundPlan(), observer);
        executor.fail(new IllegalStateException("codec service unavailable"));

        assertEquals("codec service unavailable", observer.failure.getMessage());
        assertEquals(1, executor.startedVectorIds.size());
    }

    private static BeaconBenchmarkHardwarePlan twoRoundPlan() {
        return BeaconBenchmarkHardwarePlan.parse(JsonParser.parseString(
            "{\"schemaVersion\":1,\"samplePowerBeforeAndAfterEachRound\":true," +
                "\"decoderRounds\":[" +
                "{\"vectorId\":\"vector-a\",\"codec\":\"h264\",\"profile\":\"high\",\"bitDepth\":8," +
                "\"width\":1280,\"height\":720,\"targetFps\":60,\"repetitionCount\":3}," +
                "{\"vectorId\":\"vector-b\",\"codec\":\"hevc\",\"profile\":\"main\",\"bitDepth\":8," +
                "\"width\":1280,\"height\":720,\"targetFps\":60,\"repetitionCount\":3}]}")
            .getAsJsonObject());
    }

    private static BeaconBenchmarkCompletionRequest.DecoderSample decoderSample(String codec) {
        return new BeaconBenchmarkCompletionRequest.DecoderSample(
            codec,
            "high",
            8,
            1280,
            720,
            60,
            true,
            60.0,
            5.0,
            9.0,
            0,
            0,
            false,
            false);
    }

    private static final class RecordingRoundExecutor implements DeviceBenchmarkRoundExecutor {
        private final List<String> startedVectorIds = new ArrayList<>();
        private Observer observer;
        private int cancelCount;

        @Override
        public Run start(BeaconBenchmarkHardwarePlan.DecoderRound round, Observer observer) {
            startedVectorIds.add(round.vectorId());
            this.observer = observer;
            return () -> cancelCount++;
        }

        void complete(BeaconBenchmarkCompletionRequest.DecoderSample sample) {
            Observer current = observer;
            observer = null;
            current.onCompleted(sample);
        }

        void fail(Throwable failure) {
            Observer current = observer;
            observer = null;
            current.onFailure(failure);
        }
    }

    private static final class RecordingObserver implements BeaconDeviceBenchmarkRunner.Observer {
        private boolean completed;
        private BeaconBenchmarkDeviceEvidence evidence;
        private Throwable failure;

        @Override
        public void onCompleted(BeaconBenchmarkDeviceEvidence evidence) {
            completed = true;
            this.evidence = evidence;
        }

        @Override
        public void onFailure(Throwable failure) {
            this.failure = failure;
        }
    }
}
