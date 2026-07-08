package dev.beacon.android;

public final class StreamConnectionLaunchUri {
    private StreamConnectionLaunchUri() {
    }

    public static String extract(String responseBody) {
        return StreamConnectionDescriptor.extract(responseBody).launchUri();
    }
}
