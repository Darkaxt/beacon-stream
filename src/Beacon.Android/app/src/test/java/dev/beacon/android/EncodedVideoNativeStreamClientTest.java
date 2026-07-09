package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class EncodedVideoNativeStreamClientTest {
    @Test
    public void defaultClientReportsValidContractWithoutClaimingDecodeIsConfigured() {
        EncodedVideoNativeStreamClient client = new EncodedVideoNativeStreamClient();

        NativeStreamStartResult result = client.start(validConnection());

        assertFalse(result.success());
        assertEquals("", result.status());
        assertEquals(
            "Beacon encoded video contract is valid, but no MediaCodec decoder is configured yet. codec=h264 container=annex-b video=/streams/beacon-test/color-bars.h264 1280x720@60",
            result.diagnostic());
    }

    @Test
    public void startsConfiguredDecoderForValidContract() {
        RecordingEncodedVideoDecoder decoder = new RecordingEncodedVideoDecoder(
            EncodedVideoDecodeResult.started("decoder accepted sample"));
        EncodedVideoNativeStreamClient client = new EncodedVideoNativeStreamClient(decoder);

        NativeStreamStartResult result = client.start(validConnection());

        assertTrue(result.success());
        assertEquals(
            "Native encoded video stream started. codec=h264 container=annex-b video=/streams/beacon-test/color-bars.h264 1280x720@60",
            result.status());
        assertEquals("", result.diagnostic());
        assertEquals(1, decoder.startCount);
        assertEquals("h264", decoder.lastRequest.plan().codec());
        assertEquals("annex-b", decoder.lastRequest.plan().container());
        assertEquals("/streams/beacon-test/color-bars.h264", decoder.lastRequest.plan().videoUri());
        assertEquals(1280, decoder.lastRequest.plan().width());
        assertEquals(720, decoder.lastRequest.plan().height());
        assertEquals(60, decoder.lastRequest.plan().fps());
        assertTrue(result.presentation().active());
        assertEquals("encoded-video", result.presentation().kind());
        assertEquals("/streams/beacon-test/color-bars.h264", result.presentation().endpointUri());
    }

    @Test
    public void reportsConfiguredDecoderFailure() {
        RecordingEncodedVideoDecoder decoder = new RecordingEncodedVideoDecoder(
            EncodedVideoDecodeResult.failed("decoder rejected sample"));
        EncodedVideoNativeStreamClient client = new EncodedVideoNativeStreamClient(decoder);

        NativeStreamStartResult result = client.start(validConnection());

        assertFalse(result.success());
        assertEquals("decoder rejected sample", result.diagnostic());
        assertEquals(1, decoder.startCount);
    }

    @Test
    public void reportsIncompleteContract() {
        RecordingEncodedVideoDecoder decoder = new RecordingEncodedVideoDecoder(
            EncodedVideoDecodeResult.started("decoder should not start"));
        EncodedVideoNativeStreamClient client = new EncodedVideoNativeStreamClient(decoder);
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[],\"metadata\":{\"streamKind\":\"encoded-video\"}}}}");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals(
            "Encoded video stream contract is incomplete. Missing or invalid: video endpoint, codec, container, width, height, fps.",
            result.diagnostic());
        assertEquals(0, decoder.startCount);
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

    @Test
    public void stopStopsConfiguredDecoderOnlyAfterSuccessfulStart() {
        RecordingEncodedVideoDecoder decoder = new RecordingEncodedVideoDecoder(
            EncodedVideoDecodeResult.started("decoder accepted sample"));
        EncodedVideoNativeStreamClient client = new EncodedVideoNativeStreamClient(decoder);

        client.stop();
        assertEquals(0, decoder.stopCount);

        client.start(validConnection());
        client.stop();

        assertEquals(1, decoder.stopCount);
    }

    static StreamConnectionDescriptor validConnection() {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[" +
                "{\"role\":\"video\",\"uri\":\"/streams/beacon-test/color-bars.h264\"}]," +
                "\"metadata\":{" +
                "\"streamKind\":\"encoded-video\"," +
                "\"codec\":\"h264\"," +
                "\"container\":\"annex-b\"," +
                "\"width\":\"1280\"," +
                "\"height\":\"720\"," +
                "\"fps\":\"60\"}}}}");
    }

    private static final class RecordingEncodedVideoDecoder implements EncodedVideoDecoder {
        private final EncodedVideoDecodeResult result;
        private EncodedVideoDecodeRequest lastRequest;
        private int startCount;
        private int stopCount;

        RecordingEncodedVideoDecoder(EncodedVideoDecodeResult result) {
            this.result = result;
        }

        @Override
        public EncodedVideoDecodeResult start(EncodedVideoDecodeRequest request) {
            startCount++;
            lastRequest = request;
            return result;
        }

        @Override
        public void stop() {
            stopCount++;
        }
    }
}
