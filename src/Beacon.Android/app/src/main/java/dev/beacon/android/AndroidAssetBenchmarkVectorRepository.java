package dev.beacon.android;

import android.content.Context;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;

final class AndroidAssetBenchmarkVectorRepository implements BenchmarkVectorRepository {
    private static final String H264_720P60_VECTOR =
        "beacon-h264-high-8-1280x720-60-v1";
    private static final String H264_360P30_VECTOR =
        "beacon-h264-high-8-640x360-30-v1";
    private final Context context;

    AndroidAssetBenchmarkVectorRepository(Context context) {
        if (context == null) {
            throw new IllegalArgumentException("Android context is required.");
        }
        Context applicationContext = context.getApplicationContext();
        this.context = applicationContext == null ? context : applicationContext;
    }

    @Override
    public byte[] load(String vectorId) {
        String fileName;
        if (H264_720P60_VECTOR.equals(vectorId) || H264_360P30_VECTOR.equals(vectorId)) {
            fileName = vectorId + ".h264";
        } else {
            throw new IllegalArgumentException(
                "Benchmark vector '" + vectorId + "' is not packaged by this APK.");
        }

        try (InputStream input = context.getAssets().open("benchmark-vectors/" + fileName);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[16 * 1024];
            int read;
            while ((read = input.read(buffer)) != -1) {
                output.write(buffer, 0, read);
            }
            byte[] bytes = output.toByteArray();
            if (bytes.length == 0) {
                throw new IllegalStateException("Packaged benchmark vector is empty.");
            }
            return bytes;
        } catch (IOException failure) {
            throw new IllegalStateException(
                "Could not read packaged benchmark vector '" + vectorId + "'.",
                failure);
        }
    }
}
