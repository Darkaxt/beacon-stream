package dev.beacon.android;

public final class NativeStreamClientRouter implements NativeStreamClient {
    private final NativeStreamProtocolClient[] clients;
    private NativeStreamProtocolClient activeClient;

    public NativeStreamClientRouter(NativeStreamProtocolClient... clients) {
        this.clients = clients == null ? new NativeStreamProtocolClient[0] : clients.clone();
    }

    @Override
    public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
        if (connection == null || !connection.present()) {
            return NativeStreamStartResult.unsupported("");
        }

        for (NativeStreamProtocolClient client : clients) {
            if (client != null && client.supports(connection)) {
                NativeStreamStartResult result = client.start(connection);
                if (result.success()) {
                    activeClient = client;
                }

                return result;
            }
        }

        return NativeStreamStartResult.unsupported(connection.missingLaunchUriDiagnostic());
    }

    @Override
    public void stop() {
        if (activeClient != null) {
            activeClient.stop();
            activeClient = null;
        }
    }
}
