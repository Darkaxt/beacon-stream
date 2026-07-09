package dev.beacon.android;

public final class GameStreamNativeStreamClient implements NativeStreamProtocolClient {
    @Override
    public boolean supports(StreamConnectionDescriptor connection) {
        return GameStreamEndpointPlan.from(connection).supportedProtocol();
    }

    @Override
    public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
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
}
