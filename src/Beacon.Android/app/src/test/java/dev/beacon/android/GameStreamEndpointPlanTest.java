package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class GameStreamEndpointPlanTest {
    @Test
    public void validPlanCollectsRequiredEndpointsCaseInsensitively() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"RTSP\",\"uri\":\"rtsp://127.0.0.1:48010\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");

        GameStreamEndpointPlan plan = GameStreamEndpointPlan.from(descriptor);

        assertTrue(plan.supportedProtocol());
        assertTrue(plan.complete());
        assertEquals("", plan.missingRequiredRoles());
        assertEquals(
            "rtsp=rtsp://127.0.0.1:48010, video=udp://127.0.0.1:47998, control=tcp://127.0.0.1:47999, audio=udp://127.0.0.1:48000",
            plan.requiredEndpointSummary());
    }

    @Test
    public void unsupportedProtocolIsNotComplete() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[{\"role\":\"video\",\"uri\":\"beacon-test://pattern/color-bars\"}]}}}");

        GameStreamEndpointPlan plan = GameStreamEndpointPlan.from(descriptor);

        assertFalse(plan.supportedProtocol());
        assertFalse(plan.complete());
        assertEquals("rtsp, video, control, audio", plan.missingRequiredRoles());
        assertEquals("video=beacon-test://pattern/color-bars", plan.diagnosticEndpointSummary());
    }

    @Test
    public void missingRequiredRolesAreReportedInStableOrder() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"moonlight\",\"endpoints\":[{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010\"}]}}}");

        GameStreamEndpointPlan plan = GameStreamEndpointPlan.from(descriptor);

        assertTrue(plan.supportedProtocol());
        assertFalse(plan.complete());
        assertEquals("video, control, audio", plan.missingRequiredRoles());
        assertEquals("rtsp=rtsp://127.0.0.1:48010", plan.requiredEndpointSummary());
        assertEquals("rtsp=rtsp://127.0.0.1:48010", plan.diagnosticEndpointSummary());
    }
}
