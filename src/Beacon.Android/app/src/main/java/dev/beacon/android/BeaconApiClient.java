package dev.beacon.android;

import com.google.gson.JsonObject;

import java.io.IOException;

public final class BeaconApiClient implements BeaconViewModel.BeaconService {
    private final BeaconClientConfig config;
    private final BeaconHttpTransport transport;

    public BeaconApiClient(BeaconClientConfig config) {
        this(config, new HttpUrlConnectionBeaconTransport(config));
    }

    public BeaconApiClient(BeaconClientConfig config, BeaconHttpTransport transport) {
        this.config = config;
        this.transport = transport;
    }

    @Override
    public BeaconResult hello() throws IOException {
        JsonObject body = new JsonObject();
        body.addProperty("clientId", config.clientId());
        body.addProperty("name", config.clientId());
        return post("/clients/hello", body);
    }

    @Override
    public BeaconResult patchProfile(ProfilePatch patch) throws IOException {
        return patch("/clients/" + config.clientId() + "/profile", patch.toJson());
    }

    @Override
    public BeaconResult reportCapabilities(ClientCapabilities capabilities) throws IOException {
        return post("/clients/" + config.clientId() + "/capabilities", capabilities.toJson());
    }

    @Override
    public BeaconResult reportTelemetry(ClientTelemetry telemetry) throws IOException {
        return post("/clients/" + config.clientId() + "/telemetry", telemetry.toJson());
    }

    @Override
    public BeaconResult games() throws IOException {
        return get("/games");
    }

    @Override
    public BeaconResult requestPlan(GameSelection game) throws IOException {
        return post("/clients/" + config.clientId() + "/plan", game.toJson());
    }

    @Override
    public BeaconResult launch(GameSelection game) throws IOException {
        return post("/clients/" + config.clientId() + "/launch", game.toJson());
    }

    @Override
    public BeaconResult stopStream() throws IOException {
        return post("/clients/" + config.clientId() + "/stream/stop", new JsonObject());
    }

    @Override
    public BeaconResult disconnect() throws IOException {
        return post("/clients/" + config.clientId() + "/disconnect", new JsonObject());
    }

    @Override
    public BeaconResult quit(QuitState state) throws IOException {
        return post("/clients/" + config.clientId() + "/quit", state.toJson());
    }

    @Override
    public BeaconResult emergencyRestore() throws IOException {
        return post("/clients/" + config.clientId() + "/emergency-restore", new JsonObject());
    }

    private BeaconResult post(String path, JsonObject body) throws IOException {
        return send("POST", path, body);
    }

    private BeaconResult patch(String path, JsonObject body) throws IOException {
        return send("PATCH", path, body);
    }

    private BeaconResult get(String path) throws IOException {
        return send("GET", path, null);
    }

    private BeaconResult send(String method, String path, JsonObject body) throws IOException {
        BeaconHttpResponse response = transport.send(method, path, body == null ? null : BeaconJson.gson().toJson(body));
        return new BeaconResult(response.statusCode(), response.body());
    }

    public static final class BeaconResult {
        private final int statusCode;
        private final String body;

        public BeaconResult(int statusCode, String body) {
            this.statusCode = statusCode;
            this.body = body == null ? "" : body;
        }

        public int statusCode() {
            return statusCode;
        }

        public String body() {
            return body;
        }

        public boolean isSuccess() {
            return statusCode >= 200 && statusCode <= 299;
        }

        public String summary() {
            return statusCode + " " + body;
        }
    }

    public static final class ProfilePatch {
        public Integer preferredWidth;
        public Integer preferredHeight;
        public Integer preferredRefreshHz;
        public String hdrPreference;
        public String codecPreference;
        public String qualityMode;
        public Integer bitrateCapMbps;
        public String audioMode;
        public Boolean keepAppRunningOnDisconnect;

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            add(json, "preferredWidth", preferredWidth);
            add(json, "preferredHeight", preferredHeight);
            add(json, "preferredRefreshHz", preferredRefreshHz);
            add(json, "hdrPreference", hdrPreference);
            add(json, "codecPreference", codecPreference);
            add(json, "qualityMode", qualityMode);
            add(json, "bitrateCapMbps", bitrateCapMbps);
            add(json, "audioMode", audioMode);
            add(json, "keepAppRunningOnDisconnect", keepAppRunningOnDisconnect);
            return json;
        }
    }

    public static final class ClientCapabilities {
        public boolean av1;
        public boolean hevc;
        public boolean h264;
        public boolean hdr10;
        public boolean virtualDisplayHdrSupported;
        public int maxFps;
        public boolean lowLatencyDecode;
        public String currentScreenMode;

        public ClientCapabilities(boolean av1, boolean hevc, boolean h264, boolean hdr10, boolean virtualDisplayHdrSupported) {
            this(av1, hevc, h264, hdr10, virtualDisplayHdrSupported, 120, true, null);
        }

        public ClientCapabilities(
            boolean av1,
            boolean hevc,
            boolean h264,
            boolean hdr10,
            boolean virtualDisplayHdrSupported,
            int maxFps,
            boolean lowLatencyDecode,
            String currentScreenMode) {
            this.av1 = av1;
            this.hevc = hevc;
            this.h264 = h264;
            this.hdr10 = hdr10;
            this.virtualDisplayHdrSupported = virtualDisplayHdrSupported;
            this.maxFps = maxFps;
            this.lowLatencyDecode = lowLatencyDecode;
            this.currentScreenMode = currentScreenMode;
        }

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            json.addProperty("av1", av1);
            json.addProperty("hevc", hevc);
            json.addProperty("h264", h264);
            json.addProperty("hdr10", hdr10);
            json.addProperty("virtualDisplayHdrSupported", virtualDisplayHdrSupported);
            json.addProperty("maxFps", maxFps);
            json.addProperty("lowLatencyDecode", lowLatencyDecode);
            add(json, "currentScreenMode", currentScreenMode);
            return json;
        }
    }

    public static final class ClientTelemetry {
        public int rttMs;
        public double packetLossPercent;
        public int decoderLoadPercent;
        public int estimatedBandwidthMbps;
        public String wifiBand;
        public int batteryPercent;
        public String thermalState;

        public ClientTelemetry(int rttMs, double packetLossPercent, int decoderLoadPercent) {
            this(rttMs, packetLossPercent, decoderLoadPercent, 0, null, 0, null);
        }

        public ClientTelemetry(
            int rttMs,
            double packetLossPercent,
            int decoderLoadPercent,
            int estimatedBandwidthMbps,
            String wifiBand,
            int batteryPercent,
            String thermalState) {
            this.rttMs = rttMs;
            this.packetLossPercent = packetLossPercent;
            this.decoderLoadPercent = decoderLoadPercent;
            this.estimatedBandwidthMbps = estimatedBandwidthMbps;
            this.wifiBand = wifiBand;
            this.batteryPercent = batteryPercent;
            this.thermalState = thermalState;
        }

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            json.addProperty("rttMs", rttMs);
            json.addProperty("packetLossPercent", packetLossPercent);
            json.addProperty("decoderLoadPercent", decoderLoadPercent);
            if (estimatedBandwidthMbps > 0) {
                json.addProperty("estimatedBandwidthMbps", estimatedBandwidthMbps);
            }
            add(json, "wifiBand", wifiBand);
            if (batteryPercent > 0) {
                json.addProperty("batteryPercent", batteryPercent);
            }
            add(json, "thermalState", thermalState);
            return json;
        }
    }

    public static final class GameSelection {
        public String gameId;
        public String appId;
        public String title;
        public String source;

        public static GameSelection byGameId(String gameId) {
            GameSelection selection = new GameSelection();
            selection.gameId = gameId;
            return selection;
        }

        public static GameSelection manual(String appId, String title, String source) {
            GameSelection selection = new GameSelection();
            selection.appId = appId;
            selection.title = title;
            selection.source = source;
            return selection;
        }

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            add(json, "gameId", gameId);
            add(json, "appId", appId);
            add(json, "title", title);
            add(json, "source", source);
            return json;
        }
    }

    public static final class QuitState {
        public final boolean clientActive;

        public QuitState(boolean clientActive) {
            this.clientActive = clientActive;
        }

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            json.addProperty("clientActive", clientActive);
            return json;
        }
    }

    private static void add(JsonObject json, String name, String value) {
        if (value != null && !value.trim().isEmpty()) {
            json.addProperty(name, value.trim());
        }
    }

    private static void add(JsonObject json, String name, Integer value) {
        if (value != null) {
            json.addProperty(name, value);
        }
    }

    private static void add(JsonObject json, String name, Boolean value) {
        if (value != null) {
            json.addProperty(name, value);
        }
    }
}
