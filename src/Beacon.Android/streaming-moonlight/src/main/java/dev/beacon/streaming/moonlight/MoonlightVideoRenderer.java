package dev.beacon.streaming.moonlight;

public interface MoonlightVideoRenderer {
    int DR_OK = 0;
    int DR_NEED_IDR = -1;

    int BUFFER_TYPE_PICDATA = 0;
    int BUFFER_TYPE_SPS = 1;
    int BUFFER_TYPE_PPS = 2;
    int BUFFER_TYPE_VPS = 3;

    int capabilities();

    int setup(int videoFormat, int width, int height, int fps);

    void start();

    void stop();

    void cleanup();

    int submitDecodeUnit(
        byte[] data,
        int length,
        int bufferType,
        int frameType,
        int frameNumber,
        long presentationTimeUs);
}
