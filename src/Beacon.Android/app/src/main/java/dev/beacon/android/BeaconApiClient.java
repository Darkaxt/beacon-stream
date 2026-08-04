package dev.beacon.android;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;

import java.io.IOException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import android.content.Context;

public final class BeaconApiClient implements BeaconViewModel.BeaconService {
    private final BeaconClientConfig config;
    private final BeaconHttpTransport transport;
    private final BeaconCredentialStore credentialStore;

    public BeaconApiClient(Context context, BeaconClientConfig config) {
        this(
            config,
            new HttpUrlConnectionBeaconTransport(config),
            new AndroidKeyStoreCredentialStore(context, config.clientId()));
    }

    public BeaconApiClient(BeaconClientConfig config, BeaconHttpTransport transport) {
        this(config, transport, new MemoryCredentialStore());
    }

    public BeaconApiClient(
        BeaconClientConfig config,
        BeaconHttpTransport transport,
        BeaconCredentialStore credentialStore) {
        this.config = config;
        this.transport = transport;
        this.credentialStore = credentialStore;
    }

    @Override
    public BeaconResult hello() throws IOException {
        JsonObject body = new JsonObject();
        body.addProperty("clientId", config.clientId());
        body.addProperty("name", config.clientId());
        BeaconResult response = post("/clients/hello", body);
        if (response.statusCode() != 202) {
            return response;
        }
        JsonObject pending = BeaconJson.gson().fromJson(response.body(), JsonObject.class);
        String registrationId = pending.get("registrationId").getAsString();
        BeaconResult approved = get("/clients/registrations/" + registrationId + "/completion");
        if (approved.isSuccess()) {
            JsonObject completion = BeaconJson.gson().fromJson(approved.body(), JsonObject.class);
            credentialStore.saveCredential(completion.get("credential").getAsString());
            completion.remove("credential");
            return new BeaconResult(approved.statusCode(), BeaconJson.gson().toJson(completion));
        }
        return approved;
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
    public BeaconResult beacon(boolean active) throws IOException {
        JsonObject body = new JsonObject();
        body.addProperty("active", active);
        return post("/clients/" + config.clientId() + "/beacon", body);
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

    public BeaconResult prepareBenchmark(BeaconBenchmarkPrepareRequest request) throws IOException {
        if (request == null) {
            throw new IllegalArgumentException("Benchmark preparation request is required.");
        }
        return post(
            "/clients/" + config.clientId() + "/benchmarks/prepare",
            request.toJson());
    }

    public BeaconResult completeBenchmark(
        String runId,
        BeaconBenchmarkCompletionRequest request) throws IOException {
        if (runId == null || runId.trim().isEmpty() || request == null) {
            throw new IllegalArgumentException("Benchmark run and completion evidence are required.");
        }
        return post(
            "/clients/" + config.clientId() + "/benchmarks/" + runId.trim() + "/complete",
            request.toJson());
    }

    public BeaconResult cancelBenchmark(String runId) throws IOException {
        if (runId == null || runId.trim().isEmpty()) {
            throw new IllegalArgumentException("Benchmark run is required.");
        }
        return post(
            "/clients/" + config.clientId() + "/benchmarks/" + runId.trim() + "/cancel",
            new JsonObject());
    }

    @Override
    public BeaconResult reconnect() throws IOException {
        return post("/clients/" + config.clientId() + "/reconnect", new JsonObject());
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

    private BeaconResult get(String path) throws IOException {
        return send("GET", path, null);
    }

    private BeaconResult send(String method, String path, JsonObject body) throws IOException {
        Map<String, String> headers = new HashMap<>();
        String credential = credentialStore.loadCredential();
        if (credential != null && !credential.trim().isEmpty()) {
            headers.put("Authorization", "Beacon " + credential);
            headers.put("X-Beacon-Client-Id", config.clientId());
        }
        BeaconHttpResponse response = transport.send(
            method,
            path,
            body == null ? null : BeaconJson.gson().toJson(body),
            headers);
        return new BeaconResult(response.statusCode(), response.body());
    }

    private static final class MemoryCredentialStore implements BeaconCredentialStore {
        private String credential;

        @Override
        public String loadCredential() { return credential; }

        @Override
        public void saveCredential(String value) { credential = value; }

        @Override
        public void clearCredential() { credential = null; }
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

    public static final class ClientDisplayMode {
        public final int width;
        public final int height;
        public final int refreshHz;

        public ClientDisplayMode(int width, int height, int refreshHz) {
            this.width = width;
            this.height = height;
            this.refreshHz = refreshHz;
        }

        JsonObject toJson() {
            JsonObject json = new JsonObject();
            json.addProperty("width", width);
            json.addProperty("height", height);
            json.addProperty("refreshHz", refreshHz);
            return json;
        }

        @Override
        public boolean equals(Object value) {
            if (this == value) {
                return true;
            }
            if (!(value instanceof ClientDisplayMode)) {
                return false;
            }
            ClientDisplayMode mode = (ClientDisplayMode)value;
            return width == mode.width && height == mode.height && refreshHz == mode.refreshHz;
        }

        @Override
        public int hashCode() {
            return Objects.hash(width, height, refreshHz);
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
        public ClientDisplayMode currentDisplayMode;
        public List<ClientDisplayMode> supportedDisplayModes;

        public ClientCapabilities(boolean av1, boolean hevc, boolean h264, boolean hdr10, boolean virtualDisplayHdrSupported) {
            this(
                av1,
                hevc,
                h264,
                hdr10,
                virtualDisplayHdrSupported,
                120,
                true,
                null,
                Collections.emptyList());
        }

        public ClientCapabilities(
            boolean av1,
            boolean hevc,
            boolean h264,
            boolean hdr10,
            boolean virtualDisplayHdrSupported,
            int maxFps,
            boolean lowLatencyDecode,
            ClientDisplayMode currentDisplayMode,
            List<ClientDisplayMode> supportedDisplayModes) {
            this.av1 = av1;
            this.hevc = hevc;
            this.h264 = h264;
            this.hdr10 = hdr10;
            this.virtualDisplayHdrSupported = virtualDisplayHdrSupported;
            this.maxFps = maxFps;
            this.lowLatencyDecode = lowLatencyDecode;
            this.currentDisplayMode = currentDisplayMode;
            this.supportedDisplayModes = Collections.unmodifiableList(
                new ArrayList<>(supportedDisplayModes == null ? Collections.emptyList() : supportedDisplayModes));
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
            if (currentDisplayMode != null) {
                json.add("currentDisplayMode", currentDisplayMode.toJson());
            }
            JsonArray modes = new JsonArray();
            for (ClientDisplayMode mode : supportedDisplayModes) {
                modes.add(mode.toJson());
            }
            json.add("supportedDisplayModes", modes);
            return json;
        }
    }

    public static final class ClientTelemetry {
        public Integer rttMs;
        public Double packetLossPercent;
        public Integer decoderLoadPercent;
        public Integer estimatedBandwidthMbps;
        public String wifiBand;
        public Integer batteryPercent;
        public String thermalState;

        public ClientTelemetry(int rttMs, double packetLossPercent, int decoderLoadPercent) {
            this(rttMs, packetLossPercent, decoderLoadPercent, 0, null, 0, null);
        }

        public ClientTelemetry(
            Integer rttMs,
            Double packetLossPercent,
            Integer decoderLoadPercent,
            Integer estimatedBandwidthMbps,
            String wifiBand,
            Integer batteryPercent,
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
            add(json, "rttMs", rttMs);
            add(json, "packetLossPercent", packetLossPercent);
            add(json, "decoderLoadPercent", decoderLoadPercent);
            add(json, "estimatedBandwidthMbps", estimatedBandwidthMbps);
            add(json, "wifiBand", wifiBand);
            add(json, "batteryPercent", batteryPercent);
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

    public static final class InputBatch {
        public int sequence;
        public InputEvent[] events;

        public static InputBatch pointerTap(int sequence, double x, double y) {
            InputBatch batch = new InputBatch();
            batch.sequence = sequence;
            batch.events = new InputEvent[] { InputEvent.pointer("tap", 1, x, y, 1) };
            return batch;
        }

        public static InputBatch keyboardPress(int sequence, String key, String code) {
            InputBatch batch = new InputBatch();
            batch.sequence = sequence;
            batch.events = new InputEvent[] { InputEvent.keyboard("press", key, code) };
            return batch;
        }

        public static InputBatch controller(
            int sequence,
            int controllerIndex,
            int controlId,
            int value) {
            if (sequence < 0 || controllerIndex != 0 || controlId < 1 || controlId > 21 ||
                value < Short.MIN_VALUE || value > Short.MAX_VALUE) {
                throw new IllegalArgumentException("Controller input values are invalid.");
            }
            InputBatch batch = new InputBatch();
            batch.sequence = sequence;
            batch.events = new InputEvent[] {
                InputEvent.controller(controllerIndex, controlId, value)
            };
            return batch;
        }

    }

    public static final class InputEvent {
        public String type;
        public String action;
        public Integer pointerId;
        public Double x;
        public Double y;
        public Integer buttons;
        public String key;
        public String code;
        public int controllerIndex;
        public int controlId;
        public int controllerValue;

        static InputEvent pointer(String action, int pointerId, double x, double y, Integer buttons) {
            InputEvent event = new InputEvent();
            event.type = "pointer";
            event.action = action;
            event.pointerId = pointerId;
            event.x = x;
            event.y = y;
            event.buttons = buttons;
            return event;
        }

        static InputEvent keyboard(String action, String key, String code) {
            InputEvent event = new InputEvent();
            event.type = "keyboard";
            event.action = action;
            event.key = key;
            event.code = code;
            return event;
        }

        static InputEvent controller(int controllerIndex, int controlId, int value) {
            InputEvent event = new InputEvent();
            event.type = "controller";
            event.action = "value";
            event.controllerIndex = controllerIndex;
            event.controlId = controlId;
            event.controllerValue = value;
            return event;
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

    private static void add(JsonObject json, String name, Double value) {
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
