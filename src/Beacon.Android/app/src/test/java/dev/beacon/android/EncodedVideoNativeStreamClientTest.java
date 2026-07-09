package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class EncodedVideoNativeStreamClientTest {
    @Test
    public void reportsValidContractWithoutClaimingDecodeIsImplemented() {
        EncodedVideoNativeStreamClient client = new EncodedVideoNativeStreamClient();

        NativeStreamStartResult result = client.start(validConnection());

        assertFalse(result.success());
        assertEquals("", result.status());
        assertEquals(
            "Beacon encoded video contract is valid, but MediaCodec decode is not implemented yet. codec=h264 container=annex-b video=beacon-test://video/color-bars.h264 1280x720@60",
            result.diagnostic());
    }

    @Test
    public void reportsIncompleteContract() {
        EncodedVideoNativeStreamClient client = new EncodedVideoNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[],\"metadata\":{\"streamKind\":\"encoded-video\"}}}}");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals(
            "Encoded video stream contract is incomplete. Missing or invalid: video endpoint, codec, container, width, height, fps.",
            result.diagnostic());
    }

    @Test
    public void supportsOnlyBeaconTestEncodedVideoContracts() {
        EncodedVideoNativeStreamClient client = new EncodedVideoNativeStreamClient();

        assertTrue(client.supports(validConnection()));
        assertFalse(client.supports(StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"metadata\":{\"streamKind\":\"pattern\"}}}}")));
        assertFalse(client.supports(StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"metadata\":{\"streamKind\":\"encoded-video\"}}}}")));
    }

    static StreamConnectionDescriptor validConnection() {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[" +
                "{\"role\":\"video\",\"uri\":\"beacon-test://video/color-bars.h264\"}]," +
                "\"metadata\":{" +
                "\"streamKind\":\"encoded-video\"," +
                "\"codec\":\"h264\"," +
                "\"container\":\"annex-b\"," +
                "\"width\":\"1280\"," +
                "\"height\":\"720\"," +
                "\"fps\":\"60\"}}}}");
    }
}
