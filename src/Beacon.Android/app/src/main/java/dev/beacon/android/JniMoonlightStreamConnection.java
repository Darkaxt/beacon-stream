package dev.beacon.android;

import dev.beacon.streaming.moonlight.MoonlightConnectionListener;
import dev.beacon.streaming.moonlight.MoonlightNativeConnection;
import dev.beacon.streaming.moonlight.MoonlightNativeSessionPlan;
import dev.beacon.streaming.moonlight.MoonlightNativeStartResult;
import dev.beacon.streaming.moonlight.MoonlightVideoRenderer;

final class JniMoonlightStreamConnection implements MoonlightStreamConnection {
    private final MoonlightNativeConnection connection;

    JniMoonlightStreamConnection() {
        this(new MoonlightNativeConnection());
    }

    JniMoonlightStreamConnection(MoonlightNativeConnection connection) {
        if (connection == null) {
            throw new IllegalArgumentException("Moonlight native connection is required.");
        }
        this.connection = connection;
    }

    @Override
    public MoonlightNativeStartResult start(
        MoonlightNativeSessionPlan plan,
        MoonlightVideoRenderer renderer,
        MoonlightConnectionListener listener) {
        return connection.start(plan, renderer, listener);
    }

    @Override
    public void stop() {
        connection.stop();
    }
}
