package dev.beacon.android;

public interface NativeStreamClient {
    NativeStreamStartResult start(StreamConnectionDescriptor connection);

    void stop();
}
