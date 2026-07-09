package dev.beacon.android;

public final class DiagnosticNativeStreamClient implements NativeStreamClient {
    private static final String ColorBarsEndpoint = "beacon-test://pattern/color-bars";

    @Override
    public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
        if (connection == null || !connection.present()) {
            return NativeStreamStartResult.unsupported("");
        }

        if ("beacon-test".equalsIgnoreCase(connection.protocol())) {
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

        GameStreamEndpointPlan gameStreamPlan = GameStreamEndpointPlan.from(connection);
        if (gameStreamPlan.supportedProtocol() && connection.launchUri().isEmpty()) {
            String endpointSummary = gameStreamPlan.diagnosticEndpointSummary();
            if (!gameStreamPlan.complete()) {
                return NativeStreamStartResult.unsupported(
                    "GameStream endpoint map is incomplete. Missing required endpoints: " +
                        gameStreamPlan.missingRequiredRoles() +
                        ". protocol=" + gameStreamPlan.protocol() +
                        " endpoints=" + endpointSummary);
            }

            return NativeStreamStartResult.unsupported(
                "GameStream endpoint map is complete, but native GameStream decode is not implemented yet. protocol=" +
                    gameStreamPlan.protocol() +
                    " endpoints=" + gameStreamPlan.requiredEndpointSummary());
        }

        return NativeStreamStartResult.unsupported(connection.missingLaunchUriDiagnostic());
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
