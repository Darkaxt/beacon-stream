package dev.beacon.android;

interface HdrWindowModeController {
    void setHdrEnabled(boolean enabled);

    static HdrWindowModeController noOp() {
        return enabled -> { };
    }
}
