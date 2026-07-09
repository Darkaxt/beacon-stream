package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class EncodedVideoStreamPlanTest {
    @Test
    public void acceptsCompleteBeaconTestEncodedVideoContract() {
        EncodedVideoStreamPlan plan = EncodedVideoStreamPlan.from(encodedConnection(
            "\"codec\":\"h264\",\"container\":\"annex-b\",\"width\":\"1280\",\"height\":\"720\",\"fps\":\"60\"",
            "{\"role\":\"video\",\"uri\":\"/streams/beacon-test/color-bars.h264\"}"));

        assertTrue(plan.supportedProtocol());
        assertTrue(plan.complete());
        assertEquals("/streams/beacon-test/color-bars.h264", plan.videoUri());
        assertEquals("h264", plan.codec());
        assertEquals("annex-b", plan.container());
        assertEquals(1280, plan.width());
        assertEquals(720, plan.height());
        assertEquals(60, plan.fps());
        assertEquals("", plan.diagnostic());
    }

    @Test
    public void rejectsUnsupportedProtocol() {
        EncodedVideoStreamPlan plan = EncodedVideoStreamPlan.from(connection(
            "gamestream",
            "\"streamKind\":\"encoded-video\",\"codec\":\"h264\",\"container\":\"annex-b\",\"width\":\"1280\",\"height\":\"720\",\"fps\":\"60\"",
            "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}"));

        assertFalse(plan.supportedProtocol());
        assertFalse(plan.complete());
    }

    @Test
    public void reportsMissingContractFields() {
        EncodedVideoStreamPlan plan = EncodedVideoStreamPlan.from(encodedConnection(
            "\"codec\":\"vp9\",\"container\":\"raw\",\"width\":\"0\",\"height\":\"720\",\"fps\":\"60\"",
            "{\"role\":\"control\",\"uri\":\"beacon-test://control\"}"));

        assertTrue(plan.supportedProtocol());
        assertFalse(plan.complete());
        assertEquals(
            "Encoded video stream contract is incomplete. Missing or invalid: video endpoint, codec, container, width.",
            plan.diagnostic());
    }

    private static StreamConnectionDescriptor encodedConnection(String metadata, String endpoint) {
        return connection("beacon-test", "\"streamKind\":\"encoded-video\"," + metadata, endpoint);
    }

    private static StreamConnectionDescriptor connection(String protocol, String metadata, String endpoint) {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"" + protocol + "\",\"endpoints\":[" + endpoint + "],\"metadata\":{" +
                metadata +
                "}}}}");
    }
}
