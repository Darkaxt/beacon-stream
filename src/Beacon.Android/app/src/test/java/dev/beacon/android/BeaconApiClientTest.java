package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;
import java.util.ArrayDeque;
import java.util.Arrays;
import java.util.Collections;
import java.util.Map;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class BeaconApiClientTest {
    @Test
    public void approvedCredentialIsStoredAndAttachedToScopedRequests() throws Exception {
        FakeTransport transport = new FakeTransport();
        transport.responses.add(new BeaconHttpResponse(202, "{\"registrationId\":\"registration-1\"}"));
        transport.responses.add(new BeaconHttpResponse(200, "{\"credential\":\"issued-secret\"}"));
        RecordingCredentialStore credentials = new RecordingCredentialStore();
        BeaconApiClient client = new BeaconApiClient(
            new BeaconClientConfig("http://server", "z-fold-7"),
            transport,
            credentials);

        BeaconApiClient.BeaconResult hello = client.hello();
        client.games();

        assertEquals(200, hello.statusCode());
        assertFalse(hello.body().contains("issued-secret"));
        assertEquals("issued-secret", credentials.credential);
        assertEquals("Beacon issued-secret", transport.headers.get("Authorization"));
        assertEquals("z-fold-7", transport.headers.get("X-Beacon-Client-Id"));
        assertEquals("/games", transport.path);
    }

    @Test
    public void helloSendsClientIdentityOnlyToHelloEndpoint() throws Exception {
        FakeTransport transport = new FakeTransport();
        BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server/", "z-fold-7"), transport);

        BeaconApiClient.BeaconResult result = client.hello();

        assertEquals(200, result.statusCode());
        assertEquals("POST", transport.method);
        assertEquals("/clients/hello", transport.path);
        assertTrue(transport.body.contains("\"clientId\":\"z-fold-7\""));
        assertFalse(transport.body.contains("preferredWidth"));
    }

    @Test
    public void profilePatchSerializesOnlyApkAllowedFields() throws Exception {
        FakeTransport transport = new FakeTransport();
        BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);
        BeaconApiClient.ProfilePatch patch = new BeaconApiClient.ProfilePatch();
        patch.preferredWidth = 2560;
        patch.preferredHeight = 1600;
        patch.preferredRefreshHz = 120;
        patch.hdrPreference = "prefer";
        patch.codecPreference = "av1";
        patch.qualityMode = "quality";
        patch.bitrateCapMbps = 65;
        patch.audioMode = "stereo";
        patch.keepAppRunningOnDisconnect = false;

        client.patchProfile(patch);

        assertEquals("PATCH", transport.method);
        assertEquals("/clients/z-fold-7/profile", transport.path);
        assertTrue(transport.body.contains("\"preferredWidth\":2560"));
        assertTrue(transport.body.contains("\"preferredHeight\":1600"));
        assertTrue(transport.body.contains("\"preferredRefreshHz\":120"));
        assertTrue(transport.body.contains("\"hdrPreference\":\"prefer\""));
        assertTrue(transport.body.contains("\"codecPreference\":\"av1\""));
        assertTrue(transport.body.contains("\"qualityMode\":\"quality\""));
        assertTrue(transport.body.contains("\"bitrateCapMbps\":65"));
        assertTrue(transport.body.contains("\"audioMode\":\"stereo\""));
        assertTrue(transport.body.contains("\"keepAppRunningOnDisconnect\":false"));
        assertFalse(transport.body.contains("mode"));
        assertFalse(transport.body.contains("blackout"));
        assertFalse(transport.body.contains("mirror"));
        assertFalse(transport.body.contains("restorePhysicalDisplayOnEnd"));
    }

    @Test
    public void emergencyRestorePostsToOwningClientEndpoint() throws Exception {
        FakeTransport transport = new FakeTransport();
        BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);

        client.emergencyRestore();

        assertEquals("POST", transport.method);
        assertEquals("/clients/z-fold-7/emergency-restore", transport.path);
        assertEquals("{}", transport.body);
    }

    @Test
    public void capabilitiesSerializeExpandedPlanningFacts() throws Exception {
        FakeTransport transport = new FakeTransport();
        BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);

        client.reportCapabilities(new BeaconApiClient.ClientCapabilities(true, true, true, false, false, 120, true, "2560x1600@120"));

        assertEquals("/clients/z-fold-7/capabilities", transport.path);
        assertTrue(transport.body.contains("\"maxFps\":120"));
        assertTrue(transport.body.contains("\"lowLatencyDecode\":true"));
        assertTrue(transport.body.contains("\"currentScreenMode\":\"2560x1600@120\""));
    }

    @Test
    public void telemetrySerializesExpandedPlanningFacts() throws Exception {
        FakeTransport transport = new FakeTransport();
        BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);

        client.reportTelemetry(new BeaconApiClient.ClientTelemetry(8, 0.0, 20, 120, "wifi-7", 80, "nominal"));

        assertEquals("/clients/z-fold-7/telemetry", transport.path);
        assertTrue(transport.body.contains("\"estimatedBandwidthMbps\":120"));
        assertTrue(transport.body.contains("\"wifiBand\":\"wifi-7\""));
        assertTrue(transport.body.contains("\"batteryPercent\":80"));
        assertTrue(transport.body.contains("\"thermalState\":\"nominal\""));
    }

    @Test
    public void beaconSerializesClientActivityWithoutDisplayPolicy() throws Exception {
        FakeTransport transport = new FakeTransport();
        BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);

        client.beacon(true);

        assertEquals("POST", transport.method);
        assertEquals("/clients/z-fold-7/beacon", transport.path);
        assertTrue(transport.body.contains("\"active\":true"));
        assertFalse(transport.body.contains("display"));
        assertFalse(transport.body.contains("mode"));
    }

    @Test
    public void gamesFetchesServerCatalogWithGet() throws Exception {
        FakeTransport transport = new FakeTransport();
        transport.response = new BeaconHttpResponse(200, "{\"games\":[]}");
        BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);

        BeaconApiClient.BeaconResult result = client.games();

        assertEquals(200, result.statusCode());
        assertEquals("GET", transport.method);
        assertEquals("/games", transport.path);
        assertEquals(null, transport.body);
        assertTrue(result.body().contains("\"games\""));
    }

    @Test
    public void launchConsumesServerPlanWithoutChoosingDisplayTopologyLocally() throws Exception {
        FakeTransport transport = new FakeTransport();
        transport.response = new BeaconHttpResponse(200, "{\"state\":\"streaming\",\"displayId\":\"client-z-fold-7\",\"stream\":{\"fps\":120}}");
        BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);

        BeaconApiClient.BeaconResult result = client.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals(200, result.statusCode());
        assertEquals("/clients/z-fold-7/launch", transport.path);
        assertTrue(transport.body.contains("\"gameId\":\"steam-shortcut:3767414131\""));
        assertFalse(transport.body.contains("display"));
        assertFalse(transport.body.contains("mode"));
        assertTrue(result.body().contains("\"state\":\"streaming\""));
    }

    @Test
    public void benchmarkCompletionPostsRawNetworkDecoderAndPowerFacts() throws Exception {
        FakeTransport transport = new FakeTransport();
        BeaconApiClient client = new BeaconApiClient(
            new BeaconClientConfig("http://server", "z-fold-7"),
            transport);
        BeaconStreamCore.BenchmarkNetworkResult network =
            new BeaconStreamCore.BenchmarkNetworkResult(
                96.5,
                Arrays.asList(
                    new BeaconStreamCore.BenchmarkNetworkSample(0, 1000, 2000, 0, 0, true),
                    new BeaconStreamCore.BenchmarkNetworkSample(1, 1000, 2500, 300, 1, false)));
        BeaconBenchmarkCompletionRequest completion =
            BeaconBenchmarkCompletionRequest.fromNetworkResult(
                network,
                Collections.singletonList(new BeaconBenchmarkCompletionRequest.DecoderSample(
                    "h264", "high", 8, 2560, 1600, 120, true,
                    120.0, 5.0, 9.0, 0, 0, false, false)),
                Collections.singletonList(new BeaconBenchmarkCompletionRequest.PowerSample(
                    80, false, "nominal")));

        client.completeBenchmark(
            "3c13df40-26c4-40c6-8414-268734f1024d",
            completion);

        assertEquals(
            "/clients/z-fold-7/benchmarks/3c13df40-26c4-40c6-8414-268734f1024d/complete",
            transport.path);
        assertTrue(transport.body.contains("\"rttMs\":2.5"));
        assertTrue(transport.body.contains("\"jitterMs\":0.3"));
        assertTrue(transport.body.contains("\"throughputMbps\":96.5"));
        assertTrue(transport.body.contains("\"reorderDistance\":1"));
        assertTrue(transport.body.contains("\"codec\":\"h264\""));
        assertTrue(transport.body.contains("\"thermalState\":\"nominal\""));
    }

    private static final class FakeTransport implements BeaconHttpTransport {
        String method;
        String path;
        String body;
        BeaconHttpResponse response = new BeaconHttpResponse(200, "{}");
        ArrayDeque<BeaconHttpResponse> responses = new ArrayDeque<>();
        Map<String, String> headers = Collections.emptyMap();

        @Override
        public BeaconHttpResponse send(String method, String path, String body) throws IOException {
            this.method = method;
            this.path = path;
            this.body = body;
            return responses.isEmpty() ? response : responses.remove();
        }

        @Override
        public BeaconHttpResponse send(
            String method,
            String path,
            String body,
            Map<String, String> headers) throws IOException {
            this.headers = headers;
            return send(method, path, body);
        }
    }

    private static final class RecordingCredentialStore implements BeaconCredentialStore {
        String credential;

        @Override
        public String loadCredential() { return credential; }

        @Override
        public void saveCredential(String value) { credential = value; }

        @Override
        public void clearCredential() { credential = null; }
    }
}
