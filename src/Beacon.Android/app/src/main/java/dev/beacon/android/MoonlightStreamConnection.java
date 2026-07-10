package dev.beacon.android;

import dev.beacon.streaming.moonlight.MoonlightConnectionListener;
import dev.beacon.streaming.moonlight.MoonlightNativeSessionPlan;
import dev.beacon.streaming.moonlight.MoonlightNativeStartResult;
import dev.beacon.streaming.moonlight.MoonlightVideoRenderer;

interface MoonlightStreamConnection {
    MoonlightNativeStartResult start(
        MoonlightNativeSessionPlan plan,
        MoonlightVideoRenderer renderer,
        MoonlightConnectionListener listener);

    void stop();
}
