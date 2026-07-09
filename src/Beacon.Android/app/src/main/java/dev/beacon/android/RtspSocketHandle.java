package dev.beacon.android;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

public interface RtspSocketHandle extends AutoCloseable {
    InputStream inputStream() throws IOException;

    OutputStream outputStream() throws IOException;

    @Override
    void close() throws IOException;
}
