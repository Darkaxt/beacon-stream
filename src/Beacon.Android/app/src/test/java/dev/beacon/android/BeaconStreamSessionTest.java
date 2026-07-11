package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

public final class BeaconStreamSessionTest {
    @Test
    public void parsesOnlyConnectionGrantAndDerivesHost() {
        BeaconStreamSession session = BeaconStreamSession.parse(
            "https://beacon.example:5001/control",
            "z-fold-7",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":12,\"planExplanation\":\"selected\",\"sessionId\":\"session-1\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"h264\",\"width\":1920,\"height\":1080,\"framesPerSecondNumerator\":60,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"sdr\"}}}");

        assertEquals("beacon.example", session.host());
        assertEquals(47990, session.port());
        assertEquals("session-1", session.sessionId());
        assertEquals(1920, session.selectedVideo().width());
        byte[] ticket = session.consumeTicket();
        assertEquals(3, ticket.length);
        assertTrue(session.ticketConsumed());
    }

    @Test(expected = IllegalArgumentException.class)
    public void rejectsFieldsOutsideConnectionGrantAsSubstitute() {
        BeaconStreamSession.parse("https://beacon.example", "client", "{\"ticket\":\"AQID\"}");
    }

    @Test
    public void parserPreservesOpaqueAv1HdrGrantFactsWithoutSelectingLocally() {
        BeaconStreamSession session = BeaconStreamSession.parse(
            "https://beacon.example",
            "client",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":2,\"planExplanation\":\"authoritative\",\"sessionId\":\"s\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"av1\",\"width\":2560,\"height\":1600,\"framesPerSecondNumerator\":120,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"hdr10\"}}}");

        assertEquals("av1", session.selectedVideo().codec());
        assertEquals("hdr10", session.selectedVideo().dynamicRange());
        assertEquals(2560, session.selectedVideo().width());
        assertEquals(120, session.selectedVideo().framesPerSecondNumerator());
    }
}
