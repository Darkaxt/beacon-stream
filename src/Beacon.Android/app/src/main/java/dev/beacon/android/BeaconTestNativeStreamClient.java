package dev.beacon.android;

public final class BeaconTestNativeStreamClient implements NativeStreamProtocolClient {
    private static final String ColorBarsEndpoint = "beacon-test://pattern/color-bars";

    @Override
    public boolean supports(StreamConnectionDescriptor connection) {
        return connection != null && connection.present() && "beacon-test".equalsIgnoreCase(connection.protocol());
    }

    @Override
    public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
        String colorBarsEndpoint = findColorBarsEndpoint(connection);
        if (colorBarsEndpoint.isEmpty()) {
            return NativeStreamStartResult.unsupported(
                "Beacon test stream did not include supported video endpoint beacon-test://pattern/color-bars.");
        }

        String endpoints = connection.endpointSummary();
        String endpointStatus = endpoints.isEmpty() ? "endpoints=none" : "endpoints=" + endpoints;
        return NativeStreamStartResult.started(
            "Native stream ready. protocol=beacon-test " + endpointStatus,
            NativeStreamPresentation.colorBars(colorBarsEndpoint));
    }

    @Override
    public void stop() {
    }

    private static String findColorBarsEndpoint(StreamConnectionDescriptor connection) {
        for (StreamConnectionDescriptor.Endpoint endpoint : connection.endpoints()) {
            if ("video".equalsIgnoreCase(endpoint.role()) && ColorBarsEndpoint.equalsIgnoreCase(endpoint.uri())) {
                return endpoint.uri();
            }
        }

        return "";
    }
}
