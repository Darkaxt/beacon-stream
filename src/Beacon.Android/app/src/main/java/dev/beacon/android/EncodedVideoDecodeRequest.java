package dev.beacon.android;

public final class EncodedVideoDecodeRequest {
    private final EncodedVideoStreamPlan plan;

    public EncodedVideoDecodeRequest(EncodedVideoStreamPlan plan) {
        if (plan == null) {
            throw new IllegalArgumentException("Encoded video stream plan is required.");
        }

        this.plan = plan;
    }

    public EncodedVideoStreamPlan plan() {
        return plan;
    }
}
