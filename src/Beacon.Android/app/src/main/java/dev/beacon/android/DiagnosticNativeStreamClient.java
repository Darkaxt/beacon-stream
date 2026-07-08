package dev.beacon.android;

public final class DiagnosticNativeStreamClient implements NativeStreamClient {
    @Override
    public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
        if (connection == null || !connection.present()) {
            return NativeStreamStartResult.unsupported("");
        }

        if ("beacon-test".equalsIgnoreCase(connection.protocol())) {
            String endpoints = connection.endpointSummary();
            String endpointStatus = endpoints.isEmpty() ? "endpoints=none" : "endpoints=" + endpoints;
            return NativeStreamStartResult.started("Native stream ready. protocol=beacon-test " + endpointStatus);
        }

        return NativeStreamStartResult.unsupported(connection.missingLaunchUriDiagnostic());
    }

    @Override
    public void stop() {
    }
}
