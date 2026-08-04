package dev.beacon.android;

public final class AndroidGamepadMapper {
    private static final int LEFT_TRIGGER = 16;
    private static final int RIGHT_TRIGGER = 17;
    private static final int LEFT_X = 18;
    private static final int LEFT_Y = 19;
    private static final int RIGHT_X = 20;
    private static final int RIGHT_Y = 21;

    private int nextSequence = 1;

    public BeaconApiClient.InputBatch mapButton(int keyCode, boolean pressed) {
        int controlId = buttonControlId(keyCode);
        if (controlId == 0) {
            return null;
        }
        return BeaconApiClient.InputBatch.controller(
            nextSequence++,
            0,
            controlId,
            pressed ? 1 : 0);
    }

    public BeaconApiClient.InputBatch mapAxes(
        float leftX,
        float leftY,
        float rightX,
        float rightY,
        float leftTrigger,
        float rightTrigger,
        float stickFlat,
        float triggerFlat) {
        BeaconApiClient.InputBatch batch = new BeaconApiClient.InputBatch();
        batch.sequence = nextSequence++;
        batch.events = new BeaconApiClient.InputEvent[] {
            BeaconApiClient.InputEvent.controller(0, LEFT_X, scaleAxis(leftX, stickFlat)),
            BeaconApiClient.InputEvent.controller(0, LEFT_Y, scaleAxis(-leftY, stickFlat)),
            BeaconApiClient.InputEvent.controller(0, RIGHT_X, scaleAxis(rightX, stickFlat)),
            BeaconApiClient.InputEvent.controller(0, RIGHT_Y, scaleAxis(-rightY, stickFlat)),
            BeaconApiClient.InputEvent.controller(0, LEFT_TRIGGER, scaleTrigger(leftTrigger, triggerFlat)),
            BeaconApiClient.InputEvent.controller(0, RIGHT_TRIGGER, scaleTrigger(rightTrigger, triggerFlat)),
        };
        return batch;
    }

    private static int buttonControlId(int keyCode) {
        return switch (keyCode) {
            case 19 -> 1;
            case 20 -> 2;
            case 21 -> 3;
            case 22 -> 4;
            case 108 -> 5;
            case 109 -> 6;
            case 106 -> 7;
            case 107 -> 8;
            case 102 -> 9;
            case 103 -> 10;
            case 110 -> 11;
            case 96 -> 12;
            case 97 -> 13;
            case 99 -> 14;
            case 100 -> 15;
            default -> 0;
        };
    }

    private static int scaleAxis(float value, float flat) {
        float normalized = applyFlat(value, flat);
        return normalized < 0f
            ? -Math.round(-normalized * 32768f)
            : Math.round(normalized * 32767f);
    }

    private static int scaleTrigger(float value, float flat) {
        return Math.round(applyFlat(Math.max(0f, value), flat) * 255f);
    }

    private static float applyFlat(float value, float flat) {
        if (!Float.isFinite(value)) {
            return 0f;
        }
        float clampedValue = Math.max(-1f, Math.min(1f, value));
        float clampedFlat = Float.isFinite(flat)
            ? Math.max(0f, Math.min(0.95f, flat))
            : 0f;
        float magnitude = Math.abs(clampedValue);
        if (magnitude <= clampedFlat) {
            return 0f;
        }
        float adjusted = (magnitude - clampedFlat) / (1f - clampedFlat);
        return Math.copySign(adjusted, clampedValue);
    }
}
