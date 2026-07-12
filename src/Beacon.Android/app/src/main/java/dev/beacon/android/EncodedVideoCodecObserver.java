package dev.beacon.android;

interface EncodedVideoCodecObserver {
    void onInputQueued(long presentationTimeUs, long queuedAtNs);
    void onOutputReleased(long presentationTimeUs, long releasedAtNs, boolean rendered);
    void onEndOfStream();
    void onError(Throwable failure);

    static EncodedVideoCodecObserver noOp() {
        return new EncodedVideoCodecObserver() {
            @Override public void onInputQueued(long presentationTimeUs, long queuedAtNs) { }
            @Override public void onOutputReleased(
                long presentationTimeUs, long releasedAtNs, boolean rendered) { }
            @Override public void onEndOfStream() { }
            @Override public void onError(Throwable failure) { }
        };
    }
}
