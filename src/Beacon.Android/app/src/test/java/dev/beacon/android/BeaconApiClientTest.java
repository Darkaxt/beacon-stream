package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class BeaconApiClientTest {
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
    public void inputSerializesPointerEventToOwningClientEndpoint() throws Exception {
        FakeTransport transport = new FakeTransport();
        BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);

        client.sendInput(BeaconApiClient.InputBatch.pointerTap(3, 0.5, 0.25));

        assertEquals("POST", transport.method);
        assertEquals("/clients/z-fold-7/input", transport.path);
        assertTrue(transport.body.contains("\"sequence\":3"));
        assertTrue(transport.body.contains("\"type\":\"pointer\""));
        assertTrue(transport.body.contains("\"action\":\"tap\""));
        assertTrue(transport.body.contains("\"pointerId\":1"));
        assertTrue(transport.body.contains("\"x\":0.5"));
        assertTrue(transport.body.contains("\"y\":0.25"));
        assertFalse(transport.body.contains("display"));
        assertFalse(transport.body.contains("mode"));
    }

    @Test
    public void inputSerializesKeyboardPressToOwningClientEndpoint() throws Exception {
        FakeTransport transport = new FakeTransport();
        BeaconApiClient client = new BeaconApiClient(new BeaconClientConfig("http://server", "z-fold-7"), transport);

        client.sendInput(BeaconApiClient.InputBatch.keyboardPress(4, "Escape", "Escape"));

        assertEquals("POST", transport.method);
        assertEquals("/clients/z-fold-7/input", transport.path);
        assertTrue(transport.body.contains("\"sequence\":4"));
        assertTrue(transport.body.contains("\"type\":\"keyboard\""));
        assertTrue(transport.body.contains("\"action\":\"press\""));
        assertTrue(transport.body.contains("\"key\":\"Escape\""));
        assertTrue(transport.body.contains("\"code\":\"Escape\""));
        assertFalse(transport.body.contains("display"));
        assertFalse(transport.body.contains("mode"));
    }

    private static final class FakeTransport implements BeaconHttpTransport {
        String method;
        String path;
        String body;
        BeaconHttpResponse response = new BeaconHttpResponse(200, "{}");

        @Override
        public BeaconHttpResponse send(String method, String path, String body) throws IOException {
            this.method = method;
            this.path = path;
            this.body = body;
            return response;
        }
    }
}
