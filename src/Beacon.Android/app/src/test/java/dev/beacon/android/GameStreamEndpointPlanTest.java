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

    @Test
    public void completePlanExposesRtspEndpointParts() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010/beacon/session\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");

        GameStreamEndpointPlan plan = GameStreamEndpointPlan.from(descriptor);

        assertTrue(plan.complete());
        assertTrue(plan.rtspReady());
        assertEquals("rtsp://127.0.0.1:48010/beacon/session", plan.rtspUri());
        assertEquals("127.0.0.1", plan.rtspHost());
        assertEquals(48010, plan.rtspPort());
        assertEquals("/beacon/session", plan.rtspPath());
        assertEquals("", plan.rtspDiagnostic());
    }

    @Test
    public void completePlanRejectsNonRtspEndpointScheme() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"https://127.0.0.1:48010/beacon/session\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");

        GameStreamEndpointPlan plan = GameStreamEndpointPlan.from(descriptor);

        assertTrue(plan.complete());
        assertFalse(plan.rtspReady());
        assertEquals(
            "GameStream RTSP endpoint must use rtsp://. rtsp=https://127.0.0.1:48010/beacon/session",
            plan.rtspDiagnostic());
    }
}
