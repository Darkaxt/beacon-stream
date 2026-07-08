package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;

public final class BeaconTouchInputMapperTest {
    @Test
    public void mapsTouchDownMoveAndUpToNormalizedPointerBatches() {
        BeaconTouchInputMapper mapper = new BeaconTouchInputMapper();

        BeaconApiClient.InputBatch down = mapper.map("down", 7, 1280f, 800f, 2560, 1600);
        BeaconApiClient.InputBatch move = mapper.map("move", 7, 1920f, 400f, 2560, 1600);
        BeaconApiClient.InputBatch up = mapper.map("up", 7, 2560f, 1600f, 2560, 1600);

        assertBatch(down, 1, "down", 7, 0.5, 0.5, 1);
        assertBatch(move, 2, "move", 7, 0.75, 0.25, null);
        assertBatch(up, 3, "up", 7, 1.0, 1.0, 1);
    }

    @Test
    public void clampsCoordinatesToServerAcceptedNormalizedRange() {
        BeaconTouchInputMapper mapper = new BeaconTouchInputMapper();

        BeaconApiClient.InputBatch batch = mapper.map("move", 1, -25f, 1700f, 2560, 1600);

        assertBatch(batch, 1, "move", 1, 0.0, 1.0, null);
    }

    @Test
    public void mapsMultipleTouchPointsIntoOnePointerBatch() {
        BeaconTouchInputMapper mapper = new BeaconTouchInputMapper();

        BeaconApiClient.InputBatch batch = mapper.mapPointers(
            "move",
            new int[] { 7, 9 },
            new float[] { 1280f, 2560f },
            new float[] { 800f, 0f },
            2560,
            1600);

        assertEquals(1, batch.sequence);
        assertEquals(2, batch.events.length);
        assertEvent(batch.events[0], "move", 7, 0.5, 0.5, null);
        assertEvent(batch.events[1], "move", 9, 1.0, 0.0, null);
    }

    @Test
    public void rejectsMismatchedPointerArrays() {
        BeaconTouchInputMapper mapper = new BeaconTouchInputMapper();

        IllegalArgumentException exception = org.junit.Assert.assertThrows(
            IllegalArgumentException.class,
            () -> mapper.mapPointers(
                "move",
                new int[] { 1, 2 },
                new float[] { 10f },
                new float[] { 20f, 30f },
                2560,
                1600));

        assertEquals("Pointer id and coordinate arrays must be the same non-zero length.", exception.getMessage());
    }

    @Test
    public void rejectsInvalidSurfaceGeometry() {
        BeaconTouchInputMapper mapper = new BeaconTouchInputMapper();

        IllegalArgumentException exception = org.junit.Assert.assertThrows(
            IllegalArgumentException.class,
            () -> mapper.map("down", 1, 10f, 10f, 0, 1600));

        assertEquals("Touch surface width and height must be positive.", exception.getMessage());
    }

    private static void assertBatch(
        BeaconApiClient.InputBatch batch,
        int sequence,
        String action,
        int pointerId,
        double x,
        double y,
        Integer buttons) {
        assertEquals(sequence, batch.sequence);
        assertEquals(1, batch.events.length);
        assertEvent(batch.events[0], action, pointerId, x, y, buttons);
    }

    private static void assertEvent(
        BeaconApiClient.InputEvent event,
        String action,
        int pointerId,
        double x,
        double y,
        Integer buttons) {
        assertEquals("pointer", event.type);
        assertEquals(action, event.action);
        assertEquals(pointerId, event.pointerId.intValue());
        assertEquals(x, event.x, 0.0001);
        assertEquals(y, event.y, 0.0001);
        assertEquals(buttons, event.buttons);
    }
}
