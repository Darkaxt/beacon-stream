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
        return mapPointers(
            action,
            new int[] { pointerId },
            new float[] { x },
            new float[] { y },
            surfaceWidth,
            surfaceHeight);
    }

    public BeaconApiClient.InputBatch mapPointers(
        String action,
        int[] pointerIds,
        float[] xs,
        float[] ys,
        int surfaceWidth,
        int surfaceHeight) {
        if (surfaceWidth <= 0 || surfaceHeight <= 0) {
            throw new IllegalArgumentException("Touch surface width and height must be positive.");
        }

        if (pointerIds == null ||
            xs == null ||
            ys == null ||
            pointerIds.length == 0 ||
            pointerIds.length != xs.length ||
            pointerIds.length != ys.length) {
            throw new IllegalArgumentException("Pointer id and coordinate arrays must be the same non-zero length.");
        }

        String normalizedAction = normalizeAction(action);
        BeaconApiClient.InputBatch batch = new BeaconApiClient.InputBatch();
        batch.sequence = nextSequence++;
        batch.events = new BeaconApiClient.InputEvent[pointerIds.length];
        for (int i = 0; i < pointerIds.length; i++) {
            batch.events[i] = BeaconApiClient.InputEvent.pointer(
                normalizedAction,
                pointerIds[i],
                clamp(xs[i] / surfaceWidth),
                clamp(ys[i] / surfaceHeight),
                buttonMask(normalizedAction));
        }

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
