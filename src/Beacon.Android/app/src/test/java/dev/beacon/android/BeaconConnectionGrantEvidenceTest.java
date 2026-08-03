package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertTrue;

public final class BeaconConnectionGrantEvidenceTest {
    @Test
    public void evidenceRetainsSessionIdentityWithoutRetainingTicket() {
        BeaconConnectionGrantEvidence first = BeaconConnectionGrantEvidence.fromJson(
            grant("AQID"));
        BeaconConnectionGrantEvidence second = BeaconConnectionGrantEvidence.fromJson(
            grant("BAUG"));

        assertEquals("hosted-thin-apk-session", first.sessionId());
        assertEquals(first.sessionId(), second.sessionId());
        assertNotEquals(first.ticketFingerprint(), second.ticketFingerprint());
        assertTrue(first.ticketFingerprint().matches("[0-9A-F]{64}"));
        assertTrue(!first.toString().contains("AQID"));
    }

    private static String grant(String ticket) {
        return "{\"connection\":{" +
            "\"ticket\":\"" + ticket + "\"," +
            "\"sessionId\":\"hosted-thin-apk-session\"}}";
    }
}
