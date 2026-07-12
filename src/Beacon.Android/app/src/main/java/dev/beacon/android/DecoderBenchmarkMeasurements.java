package dev.beacon.android;

import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

final class DecoderBenchmarkMeasurements {
    private final BeaconBenchmarkHardwarePlan.DecoderRound round;
    private final int expectedFrames;
    private final Map<Long, Long> inputTimesNs = new HashMap<>();
    private final List<Double> decodeLatenciesMs = new ArrayList<>();
    private final List<Double> presentationLatenciesMs = new ArrayList<>();
    private int outputFrames;
    private int renderedFrames;
    private int presentedFrames;
    private int outputErrors;
    private long firstInputNs = Long.MAX_VALUE;
    private long lastOutputNs;

    DecoderBenchmarkMeasurements(
        BeaconBenchmarkHardwarePlan.DecoderRound round,
        int expectedFrames) {
        this.round = round;
        this.expectedFrames = expectedFrames;
    }

    void recordInput(long presentationTimeUs, long queuedAtNs) {
        inputTimesNs.put(presentationTimeUs, queuedAtNs);
        firstInputNs = Math.min(firstInputNs, queuedAtNs);
    }

    void recordOutput(long presentationTimeUs, long releasedAtNs, boolean rendered) {
        outputFrames++;
        if (rendered) renderedFrames++;
        lastOutputNs = Math.max(lastOutputNs, releasedAtNs);
        recordLatency(decodeLatenciesMs, presentationTimeUs, releasedAtNs);
    }

    void recordPresentation(long presentationTimeUs, long presentedAtNs) {
        presentedFrames++;
        recordLatency(presentationLatenciesMs, presentationTimeUs, presentedAtNs);
    }

    void recordError() {
        outputErrors++;
    }

    BeaconBenchmarkCompletionRequest.DecoderSample toSample(
        boolean configured,
        int additionalOutputErrors) {
        double durationSeconds = firstInputNs == Long.MAX_VALUE || lastOutputNs <= firstInputNs
            ? 0.0
            : (lastOutputNs - firstInputNs) / 1_000_000_000.0;
        double sustainedFps = durationSeconds <= 0.0
            ? 0.0
            : outputFrames / durationSeconds;
        return new BeaconBenchmarkCompletionRequest.DecoderSample(
            round.codec(),
            round.profile(),
            round.bitDepth(),
            round.width(),
            round.height(),
            round.targetFps(),
            configured,
            sustainedFps,
            percentile95(decodeLatenciesMs),
            presentationLatenciesMs.isEmpty()
                ? null
                : percentile95(presentationLatenciesMs),
            Math.max(0, expectedFrames - presentedFrames),
            outputErrors + additionalOutputErrors,
            false,
            false);
    }

    private void recordLatency(
        List<Double> destination,
        long presentationTimeUs,
        long observedAtNs) {
        Long inputNs = inputTimesNs.get(presentationTimeUs);
        if (inputNs == null || observedAtNs < inputNs) {
            outputErrors++;
        } else {
            destination.add((observedAtNs - inputNs) / 1_000_000.0);
        }
    }

    private static double percentile95(List<Double> values) {
        if (values.isEmpty()) return 0.0;
        List<Double> sorted = new ArrayList<>(values);
        Collections.sort(sorted);
        int index = Math.max(0, (int) Math.ceil(sorted.size() * 0.95) - 1);
        return sorted.get(index);
    }
}
