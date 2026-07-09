package dev.beacon.android;

import org.junit.Test;

import java.util.Arrays;
import java.util.Collections;
import java.util.List;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class AndroidDeviceCapabilityProbeTest {
    @Test
    public void reportsSupportedDecoderCodecsAndScreenMode() {
        AndroidDeviceCapabilityProbe probe = new AndroidDeviceCapabilityProbe(new FakeCodecCatalog(Arrays.asList(
            new AndroidCodecDescriptor(false, new String[] { "video/avc" }, false, false),
            new AndroidCodecDescriptor(false, new String[] { "video/hevc", "audio/mp4a-latm" }, true, true),
            new AndroidCodecDescriptor(false, new String[] { "video/av01" }, false, false))));

        BeaconApiClient.ClientCapabilities capabilities = probe.read(2560, 1600, 120, true);

        assertTrue(capabilities.h264);
        assertTrue(capabilities.hevc);
        assertTrue(capabilities.av1);
        assertTrue(capabilities.lowLatencyDecode);
        assertTrue(capabilities.hdr10);
        assertFalse(capabilities.virtualDisplayHdrSupported);
        assertEquals(120, capabilities.maxFps);
        assertEquals("2560x1600@120", capabilities.currentScreenMode);
    }

    @Test
    public void ignoresEncodersAndUnknownMimeTypes() {
        AndroidDeviceCapabilityProbe probe = new AndroidDeviceCapabilityProbe(new FakeCodecCatalog(Arrays.asList(
            new AndroidCodecDescriptor(true, new String[] { "video/avc" }, true, true),
            new AndroidCodecDescriptor(false, new String[] { "audio/opus" }, true, true))));

        BeaconApiClient.ClientCapabilities capabilities = probe.read(1920, 1200, 60, true);

        assertFalse(capabilities.h264);
        assertFalse(capabilities.hevc);
        assertFalse(capabilities.av1);
        assertFalse(capabilities.lowLatencyDecode);
        assertFalse(capabilities.hdr10);
        assertEquals("1920x1200@60", capabilities.currentScreenMode);
    }

    @Test
    public void hdrRequiresDecoderAndScreenHdrSupport() {
        AndroidDeviceCapabilityProbe probe = new AndroidDeviceCapabilityProbe(new FakeCodecCatalog(Collections.singletonList(
            new AndroidCodecDescriptor(false, new String[] { "video/hevc" }, false, true))));

        BeaconApiClient.ClientCapabilities noScreenHdr = probe.read(2560, 1600, 120, false);
        BeaconApiClient.ClientCapabilities screenHdr = probe.read(2560, 1600, 120, true);

        assertFalse(noScreenHdr.hdr10);
        assertTrue(screenHdr.hdr10);
    }

    @Test
    public void invalidRefreshIsClampedToOneFps() {
        AndroidDeviceCapabilityProbe probe = new AndroidDeviceCapabilityProbe(new FakeCodecCatalog(Collections.singletonList(
            new AndroidCodecDescriptor(false, new String[] { "video/avc" }, false, false))));

        BeaconApiClient.ClientCapabilities capabilities = probe.read(1280, 800, 0, false);

        assertEquals(1, capabilities.maxFps);
        assertEquals("1280x800@1", capabilities.currentScreenMode);
    }

    private static final class FakeCodecCatalog implements AndroidCodecCatalog {
        private final List<AndroidCodecDescriptor> codecs;

        FakeCodecCatalog(List<AndroidCodecDescriptor> codecs) {
            this.codecs = codecs;
        }

        @Override
        public List<AndroidCodecDescriptor> codecs() {
            return codecs;
        }
    }
}
