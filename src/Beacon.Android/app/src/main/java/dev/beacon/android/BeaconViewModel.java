package dev.beacon.android;

import java.io.IOException;
import java.util.Collections;
import java.util.List;

public final class BeaconViewModel {
    private final BeaconService service;
    private final StreamConnectionLauncher connectionLauncher;
    private final NativeStreamClient nativeStreamClient;
    private final String clientId;
    private final String serverUrl;

    private String status = "Idle";
    private String latestGames = "";
    private String latestPlan = "";
    private String latestStream = "";
    private String latestNativeStream = "";
    private NativeStreamPresentation latestNativeStreamPresentation = NativeStreamPresentation.none();
    private String latestError = "";
    private boolean nativeStreamActive;
    private List<BeaconGameCatalog.GameEntry> latestGameEntries = Collections.emptyList();

    public BeaconViewModel(String clientId, String serverUrl, BeaconService service) {
        this(clientId, serverUrl, service, launchUri -> { });
    }

    public BeaconViewModel(
        String clientId,
        String serverUrl,
        BeaconService service,
        StreamConnectionLauncher connectionLauncher) {
        this(clientId, serverUrl, service, connectionLauncher, new DiagnosticNativeStreamClient());
    }

    public BeaconViewModel(
        String clientId,
        String serverUrl,
        BeaconService service,
        StreamConnectionLauncher connectionLauncher,
        NativeStreamClient nativeStreamClient) {
        this.clientId = clientId;
        this.serverUrl = serverUrl;
        this.service = service;
        this.connectionLauncher = connectionLauncher;
        this.nativeStreamClient = nativeStreamClient;
    }

    public String clientId() {
        return clientId;
    }

    public String serverUrl() {
        return serverUrl;
    }

    public String status() {
        return status;
    }

    public String latestPlan() {
        return latestPlan;
    }

    public String latestGames() {
        return latestGames;
    }

    public List<BeaconGameCatalog.GameEntry> latestGameEntries() {
        return latestGameEntries;
    }

    public String latestStream() {
        return latestStream;
    }

    public String latestNativeStream() {
        return latestNativeStream;
    }

    public NativeStreamPresentation latestNativeStreamPresentation() {
        return latestNativeStreamPresentation;
    }

    public String latestError() {
        return latestError;
    }

    public void refresh() throws IOException {
        record("hello", service.hello());
    }

    public void patchProfile(BeaconApiClient.ProfilePatch patch) throws IOException {
        record("profile patch", service.patchProfile(patch));
    }

    public void reportCapabilities(BeaconApiClient.ClientCapabilities capabilities) throws IOException {
        record("capabilities", service.reportCapabilities(capabilities));
    }

    public void reportTelemetry(BeaconApiClient.ClientTelemetry telemetry) throws IOException {
        record("telemetry", service.reportTelemetry(telemetry));
    }

    public void beacon(boolean active) throws IOException {
        record("beacon", service.beacon(active));
    }

    public void loadGames() throws IOException {
        BeaconApiClient.BeaconResult result = service.games();
        record("games", result);
        if (result.isSuccess()) {
            latestGameEntries = BeaconGameCatalog.parse(result.body());
            latestGames = BeaconGameCatalog.summarize(result.body());
        } else {
            latestGameEntries = Collections.emptyList();
            latestGames = "";
        }
    }

    public void preflight(
        BeaconApiClient.ProfilePatch patch,
        BeaconApiClient.ClientCapabilities capabilities,
        BeaconApiClient.ClientTelemetry telemetry) throws IOException {
        patchProfile(patch);
        reportCapabilities(capabilities);
        reportTelemetry(telemetry);
    }

    public void requestPlan(BeaconApiClient.GameSelection game) throws IOException {
        BeaconApiClient.BeaconResult result = service.requestPlan(game);
        record("plan", result);
        latestPlan = result.body();
    }

    public void preflightAndPlan(
        BeaconApiClient.ProfilePatch patch,
        BeaconApiClient.ClientCapabilities capabilities,
        BeaconApiClient.ClientTelemetry telemetry,
        BeaconApiClient.GameSelection game) throws IOException {
        preflight(patch, capabilities, telemetry);
        requestPlan(game);
    }

    public void launch(BeaconApiClient.GameSelection game) throws IOException {
        BeaconApiClient.BeaconResult result = service.launch(game);
        record("launch", result);
        latestStream = result.body();
        if (result.isSuccess()) {
            clearNativeStream();
            StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(result.body());
            String launchUri = connection.launchUri();
            if (!launchUri.isEmpty()) {
                connectionLauncher.launch(launchUri);
            } else {
                startNativeStream(connection);
            }
        }
    }

    private void startNativeStream(StreamConnectionDescriptor connection) {
        if (!connection.present()) {
            return;
        }

        NativeStreamStartResult start = nativeStreamClient.start(connection);
        if (start.success()) {
            latestNativeStream = start.status();
            latestNativeStreamPresentation = start.presentation();
            latestError = "";
            nativeStreamActive = true;
            return;
        }

        String diagnostic = start.diagnostic();
        if (!diagnostic.isEmpty()) {
            latestError = diagnostic;
        }
    }

    public void sendInput(BeaconApiClient.InputBatch input) throws IOException {
        record("input", service.sendInput(input));
    }

    public void preflightAndLaunch(
        BeaconApiClient.ProfilePatch patch,
        BeaconApiClient.ClientCapabilities capabilities,
        BeaconApiClient.ClientTelemetry telemetry,
        BeaconApiClient.GameSelection game) throws IOException {
        preflight(patch, capabilities, telemetry);
        launch(game);
    }

    public void stopStream() throws IOException {
        BeaconApiClient.BeaconResult result = service.stopStream();
        record("stop stream", result);
        latestStream = result.body();
        if (result.isSuccess()) {
            clearNativeStream();
        }
    }

    private void clearNativeStream() {
        if (nativeStreamActive) {
            nativeStreamClient.stop();
            nativeStreamActive = false;
        }

        latestNativeStream = "";
        latestNativeStreamPresentation = NativeStreamPresentation.none();
    }

    public void disconnect() throws IOException {
        record("disconnect", service.disconnect());
    }

    public void quit(BeaconApiClient.QuitState state) throws IOException {
        record("quit", service.quit(state));
    }

    public void emergencyRestore() throws IOException {
        record("emergency restore", service.emergencyRestore());
    }

    private void record(String action, BeaconApiClient.BeaconResult result) {
        status = action + ": " + result.statusCode();
        latestError = result.isSuccess() ? "" : result.body();
    }

    public interface BeaconService {
        BeaconApiClient.BeaconResult hello() throws IOException;

        BeaconApiClient.BeaconResult patchProfile(BeaconApiClient.ProfilePatch patch) throws IOException;

        BeaconApiClient.BeaconResult reportCapabilities(BeaconApiClient.ClientCapabilities capabilities) throws IOException;

        BeaconApiClient.BeaconResult reportTelemetry(BeaconApiClient.ClientTelemetry telemetry) throws IOException;

        BeaconApiClient.BeaconResult beacon(boolean active) throws IOException;

        BeaconApiClient.BeaconResult games() throws IOException;

        BeaconApiClient.BeaconResult requestPlan(BeaconApiClient.GameSelection game) throws IOException;

        BeaconApiClient.BeaconResult launch(BeaconApiClient.GameSelection game) throws IOException;

        BeaconApiClient.BeaconResult sendInput(BeaconApiClient.InputBatch input) throws IOException;

        BeaconApiClient.BeaconResult stopStream() throws IOException;

        BeaconApiClient.BeaconResult disconnect() throws IOException;

        BeaconApiClient.BeaconResult quit(BeaconApiClient.QuitState state) throws IOException;

        BeaconApiClient.BeaconResult emergencyRestore() throws IOException;
    }
}
