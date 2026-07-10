package dev.beacon.android;

import dev.beacon.streaming.moonlight.MoonlightNativeSessionPlan;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

public final class StreamConnectionDescriptorTest {
    @Test
    public void extractsConnectionMetadataValues() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"metadata\":{" +
                "\"streamKind\":\"encoded-video\"," +
                "\"width\":1280," +
                "\"hdr\":false," +
                "\"\":\"ignored\"}}}}");

        assertEquals("encoded-video", descriptor.metadataValue("streamKind"));
        assertEquals("1280", descriptor.metadataValue("width"));
        assertEquals("false", descriptor.metadataValue("hdr"));
        assertEquals("", descriptor.metadataValue(""));
        assertFalse(descriptor.metadata().containsKey(""));
    }

    @Test
    public void extractsValidatedNativeSessionFromOwningClientResponse() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(responseWithNativeSession(
            "gamestream",
            validNativeSessionFields()));

        assertTrue(descriptor.present());
        assertTrue(descriptor.nativeSessionProvided());
        assertTrue(descriptor.nativeSessionValid());
        assertEquals("", descriptor.nativeSessionDiagnostic());
        MoonlightNativeSessionPlan nativeSession = descriptor.nativeSession();
        assertNotNull(nativeSession);
        assertEquals("10.0.2.2", nativeSession.address());
        assertEquals(2560, nativeSession.width());
        assertEquals(1600, nativeSession.height());
        assertEquals(120, nativeSession.fps());
        assertEquals(MoonlightNativeSessionPlan.VIDEO_FORMAT_H265_MAIN10, nativeSession.supportedVideoFormats());
        assertEquals(MoonlightNativeSessionPlan.ENCFLG_ALL, nativeSession.encryptionFlags());
    }

    @Test
    public void rejectsMalformedNativeSessionWithoutDiscardingPublicConnection() {
        String malformed = validNativeSessionFields().replace(
            "\"remoteInputAesKey\":\"AAECAwQFBgcICQoLDA0ODw==\"",
            "\"remoteInputAesKey\":\"bad\"");

        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(responseWithNativeSession(
            "gamestream",
            malformed));

        assertTrue(descriptor.present());
        assertEquals("gamestream", descriptor.protocol());
        assertTrue(descriptor.nativeSessionProvided());
        assertFalse(descriptor.nativeSessionValid());
        assertNull(descriptor.nativeSession());
        assertTrue(descriptor.nativeSessionDiagnostic().contains("remoteInputAesKey"));
    }

    @Test
    public void rejectsNativeSessionForNonGameStreamProtocol() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(responseWithNativeSession(
            "beacon-test",
            validNativeSessionFields()));

        assertTrue(descriptor.present());
        assertTrue(descriptor.nativeSessionProvided());
        assertFalse(descriptor.nativeSessionValid());
        assertTrue(descriptor.nativeSessionDiagnostic().contains("protocol=gamestream"));
    }

    @Test
    public void reportsMissingRequiredNativeSessionField() {
        String missingWidth = validNativeSessionFields().replace("\"width\":2560,", "");

        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(responseWithNativeSession(
            "gamestream",
            missingWidth));

        assertTrue(descriptor.nativeSessionProvided());
        assertFalse(descriptor.nativeSessionValid());
        assertTrue(descriptor.nativeSessionDiagnostic().contains("width"));
    }

    private static String responseWithNativeSession(String protocol, String nativeSessionFields) {
        return "{" +
            "\"stream\":{\"connection\":{\"protocol\":\"" + protocol + "\",\"launchUri\":\"moonlight://beacon\"}}," +
            "\"nativeSession\":{" + nativeSessionFields + "}}";
    }

    private static String validNativeSessionFields() {
        return "\"address\":\"10.0.2.2\"," +
            "\"serverAppVersion\":\"7.1.431.0\"," +
            "\"serverGfeVersion\":\"3.27.0.120\"," +
            "\"rtspSessionUrl\":\"rtsp://10.0.2.2:48010/session/123\"," +
            "\"serverCodecModeSupport\":769," +
            "\"width\":2560," +
            "\"height\":1600," +
            "\"fps\":120," +
            "\"bitrateKbps\":45000," +
            "\"packetSize\":1024," +
            "\"streamingMode\":\"local\"," +
            "\"audioConfiguration\":\"stereo\"," +
            "\"videoFormat\":\"hevc-main10\"," +
            "\"clientRefreshRateX100\":12000," +
            "\"colorSpace\":\"rec2020\"," +
            "\"colorRange\":\"full\"," +
            "\"encryptionMode\":\"all\"," +
            "\"remoteInputAesKey\":\"AAECAwQFBgcICQoLDA0ODw==\"," +
            "\"remoteInputAesIv\":\"EBESExQVFhcYGRobHB0eHw==\"";
    }
}
