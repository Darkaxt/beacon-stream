package dev.beacon.android;

interface EncodedVideoCodecObserver {
    void onInputQueued(long frameSequence, long presentationTimeUs, long queuedAtNs);
    void onOutputReleased(
        long frameSequence,
        long presentationTimeUs,
        long releasedAtNs,
        boolean rendered);
    void onFrameRendered(
        long frameSequence,
        long presentationTimeUs,
        long renderedAtNs);
    void onEndOfStream();
    void onError(Throwable failure);
    default void onOutputFormatChanged(EncodedVideoOutputFormat format) { }

    static EncodedVideoCodecObserver noOp() {
        return new EncodedVideoCodecObserver() {
            @Override public void onInputQueued(
                long frameSequence,
                long presentationTimeUs,
                long queuedAtNs) { }
            @Override public void onOutputReleased(
                long frameSequence,
                long presentationTimeUs,
                long releasedAtNs,
                boolean rendered) { }
            @Override public void onFrameRendered(
                long frameSequence,
                long presentationTimeUs,
                long renderedAtNs) { }
            @Override public void onEndOfStream() { }
            @Override public void onError(Throwable failure) { }
        };
    }
}
