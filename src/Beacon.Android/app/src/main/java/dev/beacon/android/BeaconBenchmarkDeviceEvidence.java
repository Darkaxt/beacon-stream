package dev.beacon.android;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class BeaconBenchmarkDeviceEvidence {
    private final List<BeaconBenchmarkCompletionRequest.DecoderSample> decoderSamples;
    private final List<BeaconBenchmarkCompletionRequest.PowerSample> powerSamples;

    public BeaconBenchmarkDeviceEvidence(
        List<BeaconBenchmarkCompletionRequest.DecoderSample> decoderSamples,
        List<BeaconBenchmarkCompletionRequest.PowerSample> powerSamples) {
        if (decoderSamples == null || powerSamples == null || powerSamples.isEmpty()) {
            throw new IllegalArgumentException("Decoder evidence and at least one power sample are required.");
        }
        this.decoderSamples = Collections.unmodifiableList(new ArrayList<>(decoderSamples));
        this.powerSamples = Collections.unmodifiableList(new ArrayList<>(powerSamples));
    }

    List<BeaconBenchmarkCompletionRequest.DecoderSample> decoderSamples() {
        return decoderSamples;
    }

    List<BeaconBenchmarkCompletionRequest.PowerSample> powerSamples() {
        return powerSamples;
    }
}
