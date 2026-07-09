package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

public final class HttpEncodedVideoSampleProviderFactoryTest {
    @Test
    public void resolvesRelativeVideoPathAgainstBeaconServerUrl() {
        RecordingByteFetcher fetcher = new RecordingByteFetcher(new byte[] { 0, 0, 0, 1, 103, 1 });
        HttpEncodedVideoSampleProviderFactory factory = new HttpEncodedVideoSampleProviderFactory(
            "http://10.0.2.2:5000",
            fetcher);

        EncodedVideoSampleProvider provider = factory.create(plan("/streams/beacon-test/color-bars.h264"));

        EncodedVideoSample sample = provider.nextSample();
        assertEquals("http://10.0.2.2:5000/streams/beacon-test/color-bars.h264", fetcher.lastUrl);
        assertArrayEquals(new byte[] { 0, 0, 0, 1, 103, 1 }, sample.data());
        assertEquals(0L, sample.presentationTimeUs());
        assertTrue(provider.nextSample().endOfStream());
    }

    @Test
    public void usesAbsoluteHttpVideoUriAsIs() {
        RecordingByteFetcher fetcher = new RecordingByteFetcher(new byte[] { 0, 0, 0, 1 });
        HttpEncodedVideoSampleProviderFactory factory = new HttpEncodedVideoSampleProviderFactory(
            "http://10.0.2.2:5000",
            fetcher);

        factory.create(plan("http://192.168.1.10:5000/streams/beacon-test/color-bars.h264"));

        assertEquals("http://192.168.1.10:5000/streams/beacon-test/color-bars.h264", fetcher.lastUrl);
    }

    @Test
    public void rejectsUnsupportedEndpointScheme() {
        HttpEncodedVideoSampleProviderFactory factory = new HttpEncodedVideoSampleProviderFactory(
            "http://10.0.2.2:5000",
            new RecordingByteFetcher(new byte[] { 0, 0, 0, 1 }));

        IllegalArgumentException exception = assertThrows(
            IllegalArgumentException.class,
            () -> factory.create(plan("beacon-test://video/color-bars.h264")));

        assertEquals(
            "Encoded video endpoint scheme is not supported: beacon-test://video/color-bars.h264",
            exception.getMessage());
    }

    private static EncodedVideoStreamPlan plan(String videoUri) {
        return EncodedVideoStreamPlan.from(StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[" +
                "{\"role\":\"video\",\"uri\":\"" + videoUri + "\"}]," +
                "\"metadata\":{" +
                "\"streamKind\":\"encoded-video\"," +
                "\"codec\":\"h264\"," +
                "\"container\":\"annex-b\"," +
                "\"width\":\"2560\"," +
                "\"height\":\"1600\"," +
                "\"fps\":\"120\"}}}}"));
    }

    private static final class RecordingByteFetcher implements HttpEncodedVideoSampleProviderFactory.ByteFetcher {
        private final byte[] bytes;
        private String lastUrl = "";

        RecordingByteFetcher(byte[] bytes) {
            this.bytes = bytes;
        }

        @Override
        public byte[] fetch(String url) throws IOException {
            lastUrl = url;
            return bytes;
        }
    }
}
