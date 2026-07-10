package dev.beacon.android;

public final class EncodedVideoDecodeRequest {
    private final String codec;
    private final int width;
    private final int height;
    private final int fps;
    private final EncodedVideoSampleProvider sampleProvider;

    public EncodedVideoDecodeRequest(
        String codec,
        int width,
        int height,
        int fps,
        EncodedVideoSampleProvider sampleProvider) {
        if (codec == null || codec.isBlank()) {
            throw new IllegalArgumentException("Encoded video codec is required.");
        }
        if (width <= 0 || height <= 0 || fps <= 0) {
            throw new IllegalArgumentException("Encoded video dimensions and FPS must be positive.");
        }
        if (sampleProvider == null) {
            throw new IllegalArgumentException("Encoded video sample provider is required.");
        }

        this.codec = codec.trim();
        this.width = width;
        this.height = height;
        this.fps = fps;
        this.sampleProvider = sampleProvider;
    }

    public String codec() { return codec; }

    public int width() { return width; }

    public int height() { return height; }

    public int fps() { return fps; }

    public EncodedVideoSampleProvider sampleProvider() { return sampleProvider; }
}
