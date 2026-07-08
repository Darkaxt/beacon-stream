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
        assertEquals("", result.diagnostic());
    }

    @Test
    public void rejectsGameStreamProtocolWithoutClaimingDecodeSupport() {
        DiagnosticNativeStreamClient client = new DiagnosticNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010\"}]}}}");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals("", result.status());
        assertEquals(
            "Stream connection did not include a launch URI. protocol=gamestream endpoints=rtsp=rtsp://127.0.0.1:48010",
            result.diagnostic());
    }
}
