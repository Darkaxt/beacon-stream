package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

public final class BeaconStreamSessionTest {
    @Test
    public void parsesOnlyConnectionGrantAndDerivesHost() {
        BeaconStreamSession session = BeaconStreamSession.parse(
            "https://beacon.example:5001/control",
            "z-fold-7",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":12,\"planExplanation\":\"selected\",\"sessionId\":\"session-1\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"h264\",\"width\":1920,\"height\":1080,\"framesPerSecondNumerator\":60,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"sdr\"},\"selectedAudio\":{\"codec\":\"opus\",\"sampleRateHz\":48000,\"channelCount\":2,\"frameDurationUs\":20000,\"bitrateBps\":96000}}}");

        assertEquals("beacon.example", session.host());
        assertEquals(47990, session.port());
        assertEquals("session-1", session.sessionId());
        assertEquals(1920, session.selectedVideo().width());
        assertEquals("opus", session.selectedAudio().codec());
        assertEquals(48_000, session.selectedAudio().sampleRateHz());
        assertEquals(2, session.selectedAudio().channelCount());
        assertEquals(20_000, session.selectedAudio().frameDurationUs());
        assertEquals(96_000, session.selectedAudio().bitrateBps());
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
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":2,\"planExplanation\":\"authoritative\",\"sessionId\":\"s\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"av1\",\"width\":2560,\"height\":1600,\"framesPerSecondNumerator\":120,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"hdr10\"},\"selectedAudio\":{\"codec\":\"opus\",\"sampleRateHz\":48000,\"channelCount\":2,\"frameDurationUs\":20000,\"bitrateBps\":96000}}}");

        assertEquals("av1", session.selectedVideo().codec());
        assertEquals("hdr10", session.selectedVideo().dynamicRange());
        assertEquals(2560, session.selectedVideo().width());
        assertEquals(120, session.selectedVideo().framesPerSecondNumerator());
    }

    @Test
    public void parsesCompleteHevcMain10Hdr10GrantMetadata() {
        BeaconStreamSession session = BeaconStreamSession.parse(
            "https://beacon.example",
            "client",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":3,\"planExplanation\":\"hdr\",\"sessionId\":\"hdr-session\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"hevc\",\"width\":3840,\"height\":2160,\"framesPerSecondNumerator\":60,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"hdr10\",\"profile\":\"hevcMain10\",\"bitDepth\":10,\"colorPrimaries\":\"bt2020\",\"transferFunction\":\"pq\",\"matrixCoefficients\":\"bt2020NonConstantLuminance\",\"colorRange\":\"limited\",\"hdrStaticInfo\":\"AEiKCDk0Iaqblhn8CBM9QkDoAzIA6AOQAQ==\",\"hdrStaticInfoInBitstream\":true},\"selectedAudio\":{\"codec\":\"opus\",\"sampleRateHz\":48000,\"channelCount\":2,\"frameDurationUs\":20000,\"bitrateBps\":96000}}}");

        BeaconStreamSession.SelectedVideo video = session.selectedVideo();
        assertEquals("hevcMain10", video.profile());
        assertEquals(10, video.bitDepth());
        assertEquals("bt2020", video.colorPrimaries());
        assertEquals("pq", video.transferFunction());
        assertEquals("bt2020NonConstantLuminance", video.matrixCoefficients());
        assertEquals("limited", video.colorRange());
        assertArrayEquals(validHdrStaticInfo(), video.hdrStaticInfo());
        assertTrue(video.hdrStaticInfoInBitstream());
        byte[] copy = video.hdrStaticInfo();
        copy[0] = 99;
        assertEquals(0, video.hdrStaticInfo()[0]);
    }

    private static byte[] validHdrStaticInfo() {
        return new byte[] {
            0, 0x48, (byte) 0x8a, 0x08, 0x39, 0x34, 0x21, (byte) 0xaa,
            (byte) 0x9b, (byte) 0x96, 0x19, (byte) 0xfc, 0x08, 0x13, 0x3d,
            0x42, 0x40, (byte) 0xe8, 0x03, 0x32, 0x00, (byte) 0xe8, 0x03,
            (byte) 0x90, 0x01};
    }

    @Test
    public void parsesBenchmarkGrantWithoutInventingVideoMode() {
        BeaconStreamSession session = BeaconStreamSession.parse(
            "https://beacon.example",
            "client",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":9,\"planExplanation\":\"preflight\",\"sessionId\":\"benchmark:3c13df40-26c4-40c6-8414-268734f1024d\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"benchmark\":{\"runId\":\"3c13df40-26c4-40c6-8414-268734f1024d\",\"schemaVersion\":1,\"reliableRound\":{\"packetCount\":16,\"payloadBytes\":32768,\"measurementIntervalUs\":250000},\"datagramRound\":{\"packetCount\":64,\"payloadBytes\":1000,\"measurementIntervalUs\":250000},\"runToken\":\"AAECAwQFBgcICQoLDA0ODw==\"}}}");

        assertNull(session.selectedVideo());
        BeaconStreamSession.Benchmark benchmark = session.benchmark();
        assertNotNull(benchmark);
        assertEquals("3c13df40-26c4-40c6-8414-268734f1024d", benchmark.runId());
        assertEquals(16, benchmark.reliableRound().packetCount());
        assertEquals(32768, benchmark.reliableRound().payloadBytes());
        assertEquals(64, benchmark.datagramRound().packetCount());
        assertEquals(250000L, benchmark.datagramRound().measurementIntervalUs());
        assertEquals(16, benchmark.runToken().length);
    }
}
