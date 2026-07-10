package dev.beacon.android;

import dev.beacon.streaming.moonlight.MoonlightVideoRenderer;

interface MoonlightVideoRendererFactory {
    MoonlightVideoRenderer create();
}
