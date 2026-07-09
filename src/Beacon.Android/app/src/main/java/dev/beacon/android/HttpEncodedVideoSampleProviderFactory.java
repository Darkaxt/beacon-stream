package dev.beacon.android;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.net.URL;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public final class HttpEncodedVideoSampleProviderFactory implements EncodedVideoSampleProviderFactory {
    private final String serverUrl;
    private final ByteFetcher byteFetcher;

    public HttpEncodedVideoSampleProviderFactory(String serverUrl) {
        this(serverUrl, new UrlConnectionByteFetcher());
    }

    HttpEncodedVideoSampleProviderFactory(String serverUrl, ByteFetcher byteFetcher) {
        if (serverUrl == null || serverUrl.trim().isEmpty()) {
            throw new IllegalArgumentException("Beacon server URL is required for encoded video samples.");
        }

        if (byteFetcher == null) {
            throw new IllegalArgumentException("Encoded video byte fetcher is required.");
        }

        this.serverUrl = trimTrailingSlash(serverUrl.trim());
        this.byteFetcher = byteFetcher;
    }

    @Override
    public EncodedVideoSampleProvider create(EncodedVideoStreamPlan plan) {
        boolean framedSamples = "beacon-annexb-samples".equals(plan.sampleTransport());
        String url = resolveUrl(framedSamples ? plan.sampleUri() : plan.videoUri());
        try {
            byte[] bytes = byteFetcher.fetch(url);
            if (bytes == null || bytes.length == 0) {
                throw new IllegalArgumentException("Encoded video endpoint returned no bytes: " + url);
            }

            if (framedSamples) {
                return new SequenceSampleProvider(BeaconAnnexBSampleEnvelopeParser.parse(bytes));
            }

            List<byte[]> accessUnits = AnnexBAccessUnitSplitter.split(bytes);
            List<EncodedVideoSample> samples = new ArrayList<>();
            long frameDurationUs = 1_000_000L / plan.fps();
            for (int index = 0; index < accessUnits.size(); index++) {
                samples.add(EncodedVideoSample.data(accessUnits.get(index), index * frameDurationUs));
            }

            return new SequenceSampleProvider(samples);
        } catch (IOException ex) {
            throw new IllegalStateException("Encoded video sample fetch failed: " + ex.getMessage(), ex);
        }
    }

    private String resolveUrl(String videoUri) {
        if (videoUri == null || videoUri.trim().isEmpty()) {
            throw new IllegalArgumentException("Encoded video endpoint URI is required.");
        }

        String value = videoUri.trim();
        URI uri = URI.create(value);
        if (!uri.isAbsolute()) {
            return value.startsWith("/") ? serverUrl + value : serverUrl + "/" + value;
        }

        String scheme = uri.getScheme() == null ? "" : uri.getScheme().toLowerCase(Locale.ROOT);
        if ("http".equals(scheme) || "https".equals(scheme)) {
            return value;
        }

        throw new IllegalArgumentException("Encoded video endpoint scheme is not supported: " + value);
    }

    private static String trimTrailingSlash(String value) {
        String result = value;
        while (result.endsWith("/")) {
            result = result.substring(0, result.length() - 1);
        }

        return result;
    }

    interface ByteFetcher {
        byte[] fetch(String url) throws IOException;
    }

    private static final class UrlConnectionByteFetcher implements ByteFetcher {
        @Override
        public byte[] fetch(String url) throws IOException {
            HttpURLConnection connection = (HttpURLConnection) new URL(url).openConnection();
            connection.setRequestMethod("GET");
            connection.setRequestProperty("Accept", "video/H264, application/octet-stream");
            try {
                int statusCode = connection.getResponseCode();
                if (statusCode < 200 || statusCode > 299) {
                    throw new IOException("HTTP " + statusCode + " from " + url);
                }

                try (InputStream input = connection.getInputStream(); ByteArrayOutputStream output = new ByteArrayOutputStream()) {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = input.read(buffer)) >= 0) {
                        output.write(buffer, 0, read);
                    }

                    return output.toByteArray();
                }
            } finally {
                connection.disconnect();
            }
        }
    }

    private static final class SequenceSampleProvider implements EncodedVideoSampleProvider {
        private final List<EncodedVideoSample> samples;
        private int index;

        SequenceSampleProvider(List<EncodedVideoSample> samples) {
            this.samples = new ArrayList<>(samples);
        }

        @Override
        public EncodedVideoSample nextSample() {
            if (index >= samples.size()) {
                return EncodedVideoSample.eos();
            }

            EncodedVideoSample sample = samples.get(index);
            index++;
            return sample;
        }
    }
}
