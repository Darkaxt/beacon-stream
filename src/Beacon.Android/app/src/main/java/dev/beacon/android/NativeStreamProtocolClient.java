package dev.beacon.android;

interface NativeStreamProtocolClient extends NativeStreamClient {
    boolean supports(StreamConnectionDescriptor connection);
}
