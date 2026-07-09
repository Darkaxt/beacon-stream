package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class DiagnosticNativeStreamClientTest {
    @Test
    public void startsBeaconTestProtocolWithEndpointStatus() {
        DiagnosticNativeStreamClient client = new DiagnosticNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[{\"role\":\"video\",\"uri\":\"beacon-test://pattern/color-bars\"}]}}}");

        NativeStreamStartResult result = client.start(connection);

        assertTrue(result.success());
        assertEquals(
            "Native stream ready. protocol=beacon-test endpoints=video=beacon-test://pattern/color-bars",
            result.status());
        assertTrue(result.presentation().active());
        assertEquals("color-bars", result.presentation().kind());
        assertEquals("beacon-test://pattern/color-bars", result.presentation().endpointUri());
        assertEquals("", result.diagnostic());
    }

    @Test
    public void defaultFacadeKeepsBeaconTestRouting() {
        NativeStreamClient client = new DiagnosticNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[{\"role\":\"video\",\"uri\":\"beacon-test://pattern/color-bars\"}]}}}");

        NativeStreamStartResult result = client.start(connection);

        assertTrue(result.success());
        assertEquals("color-bars", result.presentation().kind());
    }

    @Test
    public void rejectsBeaconTestProtocolWithoutSupportedVideoPattern() {
        DiagnosticNativeStreamClient client = new DiagnosticNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[{\"role\":\"control\",\"uri\":\"beacon-test://control\"}]}}}");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals("", result.status());
        assertFalse(result.presentation().active());
        assertEquals(
            "Beacon test stream did not include supported video endpoint beacon-test://pattern/color-bars.",
            result.diagnostic());
    }

    @Test
    public void rejectsIncompleteGameStreamEndpointMapWithoutGenericMissingLaunchUriDiagnostic() {
        DiagnosticNativeStreamClient client = new DiagnosticNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010\"}]}}}");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals("", result.status());
        assertFalse(result.presentation().active());
        assertEquals(
            "GameStream endpoint map is incomplete. Missing required endpoints: video, control, audio. protocol=gamestream endpoints=rtsp=rtsp://127.0.0.1:48010",
            result.diagnostic());
    }

    @Test
    public void rejectsCompleteGameStreamEndpointMapWithDecoderNotImplementedDiagnostic() {
        DiagnosticNativeStreamClient client = new DiagnosticNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals("", result.status());
        assertFalse(result.presentation().active());
        assertEquals(
            "GameStream endpoint map is complete, but native GameStream decode is not implemented yet. protocol=gamestream endpoints=rtsp=rtsp://127.0.0.1:48010, video=udp://127.0.0.1:47998, control=tcp://127.0.0.1:47999, audio=udp://127.0.0.1:48000",
            result.diagnostic());
    }
}
