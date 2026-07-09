package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class BeaconTestNativeStreamClientTest {
    @Test
    public void startsColorBarsEndpoint() {
        BeaconTestNativeStreamClient client = new BeaconTestNativeStreamClient();
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
    }

    @Test
    public void rejectsBeaconTestProtocolWithoutColorBarsEndpoint() {
        BeaconTestNativeStreamClient client = new BeaconTestNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[{\"role\":\"control\",\"uri\":\"beacon-test://control\"}]}}}");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals(
            "Beacon test stream did not include supported video endpoint beacon-test://pattern/color-bars.",
            result.diagnostic());
    }

    @Test
    public void supportsOnlyBeaconTestProtocol() {
        BeaconTestNativeStreamClient client = new BeaconTestNativeStreamClient();

        assertTrue(client.supports(connection("beacon-test")));
        assertFalse(client.supports(connection("gamestream")));
        assertFalse(client.supports(null));
    }

    private static StreamConnectionDescriptor connection(String protocol) {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"" + protocol + "\"}}}");
    }
}
