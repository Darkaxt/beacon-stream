package dev.beacon.android;

import java.util.Locale;

public final class BeaconTouchInputMapper {
    private int nextSequence = 1;

    public BeaconApiClient.InputBatch map(
        String action,
        int pointerId,
        float x,
        float y,
        int surfaceWidth,
        int surfaceHeight) {
        if (surfaceWidth <= 0 || surfaceHeight <= 0) {
            throw new IllegalArgumentException("Touch surface width and height must be positive.");
        }

        String normalizedAction = normalizeAction(action);
        BeaconApiClient.InputBatch batch = new BeaconApiClient.InputBatch();
        batch.sequence = nextSequence++;
        batch.events = new BeaconApiClient.InputEvent[] {
            BeaconApiClient.InputEvent.pointer(
                normalizedAction,
                pointerId,
                clamp(x / surfaceWidth),
                clamp(y / surfaceHeight),
                buttonMask(normalizedAction))
        };
        return batch;
    }

    private static String normalizeAction(String action) {
        if (action == null) {
            throw new IllegalArgumentException("Touch action is required.");
        }

        String normalized = action.trim().toLowerCase(Locale.ROOT);
        if (normalized.equals("down") || normalized.equals("move") || normalized.equals("up") || normalized.equals("tap")) {
            return normalized;
        }

        throw new IllegalArgumentException("Unsupported touch action '" + action + "'.");
    }

    private static Integer buttonMask(String action) {
        return action.equals("move") ? null : 1;
    }

    private static double clamp(float value) {
        if (value < 0f) {
            return 0d;
        }

        if (value > 1f) {
            return 1d;
        }

        return value;
    }
}
