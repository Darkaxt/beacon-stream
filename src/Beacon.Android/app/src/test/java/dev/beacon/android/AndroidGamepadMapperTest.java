package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;

public final class AndroidGamepadMapperTest {
    @Test
    public void mapsXboxFaceButtonPressAndRelease() {
        AndroidGamepadMapper mapper = new AndroidGamepadMapper();

        BeaconApiClient.InputBatch pressed = mapper.mapButton(96, true);
        BeaconApiClient.InputBatch released = mapper.mapButton(96, false);

        assertEvent(pressed, 1, 12, 1);
        assertEvent(released, 2, 12, 0);
    }

    @Test
    public void mapsDirectionalAndSystemButtons() {
        AndroidGamepadMapper mapper = new AndroidGamepadMapper();

        assertEvent(mapper.mapButton(19, true), 1, 1, 1);
        assertEvent(mapper.mapButton(22, true), 2, 4, 1);
        assertEvent(mapper.mapButton(108, true), 3, 5, 1);
        assertEvent(mapper.mapButton(109, true), 4, 6, 1);
        assertEvent(mapper.mapButton(110, true), 5, 11, 1);
    }

    @Test
    public void mapsSticksTriggersDeadZonesAndYDirection() {
        AndroidGamepadMapper mapper = new AndroidGamepadMapper();

        BeaconApiClient.InputBatch batch = mapper.mapAxes(
            0.05f,
            0.05f,
            1f,
            -1f,
            0.05f,
            1f,
            0.1f,
            0.1f);

        assertEquals(1, batch.sequence);
        assertEquals(6, batch.events.length);
        assertController(batch.events[0], 18, 0);
        assertController(batch.events[1], 19, 0);
        assertController(batch.events[2], 20, 32767);
        assertController(batch.events[3], 21, 32767);
        assertController(batch.events[4], 16, 0);
        assertController(batch.events[5], 17, 255);
    }

    @Test
    public void clampsValuesBeforeScaling() {
        AndroidGamepadMapper mapper = new AndroidGamepadMapper();

        BeaconApiClient.InputBatch batch = mapper.mapAxes(
            2f,
            -2f,
            -2f,
            2f,
            -1f,
            2f,
            0f,
            0f);

        assertController(batch.events[0], 18, 32767);
        assertController(batch.events[1], 19, 32767);
        assertController(batch.events[2], 20, -32768);
        assertController(batch.events[3], 21, -32768);
        assertController(batch.events[4], 16, 0);
        assertController(batch.events[5], 17, 255);
    }

    @Test
    public void ignoresKeysThatAreNotGamepadControls() {
        AndroidGamepadMapper mapper = new AndroidGamepadMapper();

        assertNull(mapper.mapButton(29, true));
    }

    private static void assertEvent(
        BeaconApiClient.InputBatch batch,
        int sequence,
        int controlId,
        int value) {
        assertEquals(sequence, batch.sequence);
        assertEquals(1, batch.events.length);
        assertController(batch.events[0], controlId, value);
    }

    private static void assertController(
        BeaconApiClient.InputEvent event,
        int controlId,
        int value) {
        assertEquals("controller", event.type);
        assertEquals("value", event.action);
        assertEquals(0, event.controllerIndex);
        assertEquals(controlId, event.controlId);
        assertEquals(value, event.controllerValue);
    }
}
