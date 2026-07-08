package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;

public final class StreamConnectionLaunchUriTest {
    @Test
    public void extractReturnsServerConnectionLaunchUri() {
        String uri = StreamConnectionLaunchUri.extract(
            "{\"stream\":{\"connection\":{\"launchUri\":\"moonlight://stream/z-fold-7\"}}}");

        assertEquals("moonlight://stream/z-fold-7", uri);
    }

    @Test
    public void extractReturnsEmptyWhenConnectionIsMissing() {
        assertEquals("", StreamConnectionLaunchUri.extract("{}"));
    }

    @Test
    public void extractReturnsEmptyWhenLaunchUriIsBlank() {
        assertEquals("", StreamConnectionLaunchUri.extract("{\"stream\":{\"connection\":{\"launchUri\":\"\"}}}"));
    }
}
