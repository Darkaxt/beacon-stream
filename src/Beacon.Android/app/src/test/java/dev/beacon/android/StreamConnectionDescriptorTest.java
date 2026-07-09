package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;

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
}
