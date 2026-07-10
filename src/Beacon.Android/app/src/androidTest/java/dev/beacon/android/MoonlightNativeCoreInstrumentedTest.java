package dev.beacon.android;

import junit.framework.TestCase;

import dev.beacon.streaming.moonlight.MoonlightNativeCore;

public final class MoonlightNativeCoreInstrumentedTest extends TestCase {
    public void testNativeCoreLoadsAndReportsStableIdentity() {
        assertTrue(MoonlightNativeCore.isAvailable());
        assertEquals("moonlight-common-c", MoonlightNativeCore.identity());
        assertEquals("platform initialization", MoonlightNativeCore.stageName(1));
    }
}
