package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class GameStreamNativeStreamClientTest {
    @Test
    public void rejectsIncompleteGameStreamEndpointMap() {
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010\"}]}}}");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals(
            "GameStream endpoint map is incomplete. Missing required endpoints: video, control, audio. protocol=gamestream endpoints=rtsp=rtsp://127.0.0.1:48010",
            result.diagnostic());
    }

    @Test
    public void rejectsCompleteGameStreamEndpointMapWithDecoderNotImplementedDiagnostic() {
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals(
            "GameStream endpoint map is complete, but native GameStream decode is not implemented yet. protocol=gamestream endpoints=rtsp=rtsp://127.0.0.1:48010, video=udp://127.0.0.1:47998, control=tcp://127.0.0.1:47999, audio=udp://127.0.0.1:48000",
            result.diagnostic());
    }

    @Test
    public void supportsGameStreamAndMoonlightProtocols() {
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient();

        assertTrue(client.supports(connection("gamestream")));
        assertTrue(client.supports(connection("moonlight")));
        assertFalse(client.supports(connection("beacon-test")));
        assertFalse(client.supports(null));
    }

    private static StreamConnectionDescriptor connection(String protocol) {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"" + protocol + "\"}}}");
    }
}
